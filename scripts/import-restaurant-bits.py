"""Install user-provided Restaurant Bits or CartoonTown art without executing scripts.

The Unity Store edition is kept outside version control; original metadata GUIDs
preserve model/material/texture references. Only art files are accepted.
"""
import argparse
import json
from pathlib import Path, PurePosixPath
import tarfile


def install(package: Path, project: Path, marker="KayKit - Restaurant Bits (For Unity)/", folder="RestaurantBits", min_assets=400):
    if not folder.isalnum():
        raise ValueError("Resource folder must be a single alphanumeric name")
    destination = project / "Assets/Resources" / folder
    count = 0
    inventory = []
    with tarfile.open(package, "r:*") as archive:
        members = {member.name: member for member in archive.getmembers() if member.isfile()}
        for key, member in members.items():
            if not key.endswith("/pathname"):
                continue
            # This supplied archive contains newline padding after pathnames.
            original = archive.extractfile(member).read().decode("utf-8-sig").splitlines()[0].rstrip("\x00")
            if marker not in original:
                continue
            relative = PurePosixPath(original.split(marker, 1)[1].replace("\\", "/"))
            if relative.is_absolute() or ".." in relative.parts or any(":" in part for part in relative.parts):
                raise ValueError(f"Unsafe package asset path: {original!r}")
            if relative.suffix.lower() not in {".fbx", ".mat", ".png", ".prefab", ".tga", ".tif"}:
                continue
            prefix = key.rsplit("/", 1)[0]
            asset = members.get(prefix + "/asset")
            if asset is None:
                continue
            target = destination.joinpath(*relative.parts).resolve()
            if not target.is_relative_to(destination.resolve()):
                raise ValueError(f"Package asset escapes destination: {original!r}")
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.extractfile(asset).read())
            metadata = members.get(prefix + "/asset.meta")
            if metadata:
                Path(str(target) + ".meta").write_bytes(archive.extractfile(metadata).read())
            count += 1
            inventory.append(str(relative))
    if count < min_assets:
        raise ValueError(f"Incomplete art package: only {count} files recognized")
    (destination / "LOCAL_SOURCE.txt").write_text(
        "User-supplied Unity art: "+package.name+". Local art dependency, excluded from Git.\n"
        "Do not redistribute these source assets under the free edition's CC0 license.\n",
        encoding="utf-8",
    )
    print(json.dumps({"installed": count, "destination": str(destination)}, ensure_ascii=False))


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, nargs="?", default=root / "unity/Restaurant Bits for Unity v1.0.0 (18 Jul 2024).unitypackage")
    parser.add_argument("--project", type=Path, default=root / "unity/OvercookedViewer")
    parser.add_argument("--asset-root", default="KayKit - Restaurant Bits (For Unity)/")
    parser.add_argument("--resource-folder", default="RestaurantBits")
    args = parser.parse_args()
    install(args.package.resolve(), args.project.resolve(), marker=args.asset_root, folder=args.resource_folder)
