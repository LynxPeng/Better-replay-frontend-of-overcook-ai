"""Package a cross-built .app with Unix permissions, without requiring macOS."""
import argparse
import hashlib
import plistlib
import stat
import struct
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


def macho_architectures(path):
    with path.open("rb") as stream:
        header = stream.read(8)
        if len(header) < 8:
            return set()
        magic = header[:4]
        names = {0x01000007: "x86_64", 0x0100000C: "arm64"}
        if magic in (b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe"):
            return {names.get(struct.unpack("<I", header[4:])[0], "unknown")}
        if magic in (b"\xfe\xed\xfa\xcf", b"\xfe\xed\xfa\xce"):
            return {names.get(struct.unpack(">I", header[4:])[0], "unknown")}
        if magic not in (b"\xca\xfe\xba\xbe", b"\xca\xfe\xba\xbf"):
            return set()
        count = struct.unpack(">I", header[4:])[0]
        if not 1 <= count <= 16:
            raise ValueError(f"Invalid Mach-O architecture table: {path}")
        record_size = 32 if magic[-1] == 0xBF else 20
        result = set()
        for _ in range(count):
            entry = stream.read(record_size)
            if len(entry) != record_size:
                raise ValueError(f"Truncated Mach-O architecture table: {path}")
            result.add(names.get(struct.unpack(">I", entry[:4])[0], "unknown"))
        return result


def validate_app(app):
    plist_path = app / "Contents/Info.plist"
    if not app.name.endswith(".app") or not plist_path.is_file():
        raise ValueError(f"Missing app Info.plist: {app}")
    with plist_path.open("rb") as stream:
        executable_name = plistlib.load(stream).get("CFBundleExecutable", "")
    if not executable_name or Path(executable_name).name != executable_name:
        raise ValueError("Invalid CFBundleExecutable")
    executable = app / "Contents/MacOS" / executable_name
    if not executable.is_file() or not macho_architectures(executable):
        raise ValueError(f"Missing Mach-O executable: {executable}")
    return executable


def archive_app(app, destination, extras=()):
    app, destination = Path(app), Path(destination)
    executable = validate_app(app)
    # Exclusive creation protects previously published artifacts.
    with ZipFile(destination, "x", compression=ZIP_DEFLATED, compresslevel=6) as archive:
        for entry in [app, *sorted(app.rglob("*"))]:
            name = entry.relative_to(app.parent).as_posix()
            info = ZipInfo(name + ("/" if entry.is_dir() else ""))
            info.create_system = 3
            if entry.is_symlink():
                info.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(info, entry.readlink().as_posix().encode("utf-8"))
                continue
            if entry.is_dir():
                info.external_attr = ((stat.S_IFDIR | 0o755) << 16) | 0x10
                archive.writestr(info, b"")
                continue
            mode = 0o755 if entry == executable or macho_architectures(entry) else 0o644
            info.external_attr = (stat.S_IFREG | mode) << 16
            info.compress_type = ZIP_DEFLATED
            with entry.open("rb") as source, archive.open(info, "w") as target:
                while block := source.read(1024 * 1024):
                    target.write(block)
        for source, name in extras:
            info = ZipInfo(name)
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            info.compress_type = ZIP_DEFLATED
            archive.writestr(info, Path(source).read_bytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--version", default="v0.1.2-macos-preview")
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    executable = validate_app(args.app)
    architectures = macho_architectures(executable)
    if architectures != {"x86_64", "arm64"}:
        raise ValueError(f"Universal app expected, found {sorted(architectures)}")
    destination = args.out or repo / "release" / f"BetterReplay-macOS-Universal-{args.version}.zip"
    destination.parent.mkdir(parents=True, exist_ok=True)
    extras = [(repo / name, name) for name in ("LICENSE", "THIRD_PARTY_NOTICES.md")]
    extras += [(path, path.relative_to(repo).as_posix()) for path in sorted((repo / "LICENSES").rglob("*")) if path.is_file()]
    extras += [(repo / "MACOS-QUICKSTART.md", "README.md")]
    extras += [(repo / "unity/OvercookedViewer/Assets/StreamingAssets/CharacterCredits.txt", "CharacterCredits.txt")]
    archive_app(args.app, destination, extras)
    hasher = hashlib.sha256()
    with destination.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            hasher.update(block)
    digest = hasher.hexdigest()
    destination.with_suffix(destination.suffix + ".sha256").write_text(f"{digest}  {destination.name}\n", encoding="ascii")
    print(f"{destination}\nArchitectures: {sorted(architectures)}\nSHA256: {digest}")


if __name__ == "__main__":
    main()
