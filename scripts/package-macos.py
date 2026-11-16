"""Package a cross-built .app with Unix permissions, without requiring macOS."""
import argparse
import hashlib
import plistlib
import posixpath
import shutil
import stat
import struct
import tarfile
from pathlib import Path, PurePosixPath
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


def archive_host(archive, directory, source):
    has_executable = False
    with tarfile.open(source, "r|gz") as host:
        for member in host:
            path = PurePosixPath(member.name)
            if not path.parts or path.parts[0] != "OvercookedHost" or ".." in path.parts:
                raise ValueError(f"Unexpected host member: {member.name}")
            relative = path.relative_to("OvercookedHost")
            name = (directory / relative).as_posix()
            info = ZipInfo(name + ("/" if member.isdir() else ""))
            info.create_system = 3
            if member.isdir():
                info.external_attr = ((stat.S_IFDIR | member.mode) << 16) | 0x10
                archive.writestr(info, b"")
            elif member.issym() or member.islnk():
                link = member.linkname
                if member.islnk():
                    link = posixpath.relpath(link, path.parent.as_posix())
                resolved = posixpath.normpath(posixpath.join(path.parent.as_posix(), link))
                if not resolved.startswith("OvercookedHost/"):
                    raise ValueError(f"Host link escapes its directory: {member.name}")
                info.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(info, link.encode("utf-8"))
            elif member.isfile():
                info.external_attr = (stat.S_IFREG | member.mode) << 16
                info.compress_type = ZIP_DEFLATED
                info.file_size = member.size
                with host.extractfile(member) as stream, archive.open(info, "w") as target:
                    shutil.copyfileobj(stream, target, 1024 * 1024)
                if relative.as_posix() == "OvercookedHost":
                    has_executable = bool(member.mode & stat.S_IXUSR)
            else:
                raise ValueError(f"Unsupported host member: {member.name}")
    if not has_executable:
        raise ValueError(f"Missing executable in {source}")


def archive_app(app, destination, extras=(), hosts=()):
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
            with Path(source).open("rb") as stream, archive.open(info, "w") as target:
                shutil.copyfileobj(stream, target, 1024 * 1024)
        for arch, source in hosts:
            archive_host(archive, PurePosixPath(app.name) / "Contents/Resources/HostRuntime" / arch, source)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("--version", default="v0.1.3-macos-preview")
    parser.add_argument("--out", type=Path)
    parser.add_argument("--host-arm64", type=Path)
    parser.add_argument("--host-x86-64", type=Path)
    parser.add_argument("--models", type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    executable = validate_app(args.app)
    architectures = macho_architectures(executable)
    if architectures != {"x86_64", "arm64"}:
        raise ValueError(f"Universal app expected, found {sorted(architectures)}")
    live = args.app.name == "OvercookedLive.app"
    host_args = (args.host_arm64, args.host_x86_64, args.models)
    if live and not all(host_args):
        parser.error("The complete online app needs both --host archives and --models")
    if not live and any(host_args):
        parser.error("Host archives are only used by OvercookedLive.app")
    product = "OvercookedLive" if live else "BetterReplay"
    destination = args.out or repo / "release" / f"{product}-macOS-Universal-{args.version}.zip"
    destination.parent.mkdir(parents=True, exist_ok=True)
    extras = [(repo / name, name) for name in ("LICENSE", "THIRD_PARTY_NOTICES.md")]
    extras += [(path, path.relative_to(repo).as_posix()) for path in sorted((repo / "LICENSES").rglob("*")) if path.is_file()]
    quickstart = "MACOS-LIVE-QUICKSTART.md" if live else "MACOS-QUICKSTART.md"
    extras += [(repo / quickstart, "README.md")]
    extras += [(repo / "unity/OvercookedViewer/Assets/StreamingAssets/CharacterCredits.txt", "CharacterCredits.txt")]
    hosts = ()
    if live:
        hosts = (("arm64", args.host_arm64), ("x86_64", args.host_x86_64))
        runtime = f"{args.app.name}/Contents/Resources/HostRuntime"
        extras += [(args.models / name, f"{runtime}/Models/{name}")
                   for name in ("ppo-final.zip", "sp-cramped-room.npz")]
    for source in [path for path, _ in extras] + [path for _, path in hosts]:
        if not source.is_file():
            raise FileNotFoundError(source)
    archive_app(args.app, destination, extras, hosts)
    hasher = hashlib.sha256()
    with destination.open("rb") as stream:
        while block := stream.read(1024 * 1024):
            hasher.update(block)
    digest = hasher.hexdigest()
    destination.with_suffix(destination.suffix + ".sha256").write_text(f"{digest}  {destination.name}\n", encoding="ascii")
    print(f"{destination}\nArchitectures: {sorted(architectures)}\nSHA256: {digest}")


if __name__ == "__main__":
    main()
