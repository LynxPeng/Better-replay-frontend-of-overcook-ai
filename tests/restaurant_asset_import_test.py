"""The local art installer must never write outside its destination."""
import importlib.util
import io
from pathlib import Path
import tarfile

import pytest


spec = importlib.util.spec_from_file_location("restaurant_asset_installer", Path(__file__).parents[1] / "scripts/import-restaurant-bits.py")
installer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(installer)


@pytest.mark.parametrize("suffix", ["..\\..\\..\\escaped.png", "../../../escaped.png"])
def test_art_package_cannot_escape_destination(tmp_path, suffix):
    package = tmp_path / "unsafe.unitypackage"
    with tarfile.open(package, "w:gz") as archive:
        files = {
            "entry/pathname": ("Assets/KayKit - Restaurant Bits (For Unity)/" + suffix).encode(),
            "entry/asset": b"not an actual texture",
            "entry/asset.meta": b"not actual metadata",
        }
        for name, content in files.items():
            entry = tarfile.TarInfo(name)
            entry.size = len(content)
            archive.addfile(entry, io.BytesIO(content))
    project = tmp_path / "project"
    with pytest.raises(ValueError):
        installer.install(package, project)
    assert not (project / "escaped.png").exists()
    assert not (project / "escaped.png.meta").exists()


def test_uncompressed_town_package_installs_art_without_scripts(tmp_path):
    package=tmp_path/"town.unitypackage"
    with tarfile.open(package,"w") as archive:
        for prefix,path,content in [("model","Assets/Town/Models/Tree.fbx",b"mesh"),("code","Assets/Town/Legacy.cs",b"untrusted code")]:
            for suffix,data in [("pathname",path.encode()),("asset",content)]:
                entry=tarfile.TarInfo(prefix+"/"+suffix);entry.size=len(data);archive.addfile(entry,io.BytesIO(data))
    installer.install(package,tmp_path/"project",marker="Assets/Town/",folder="Town",min_assets=1)
    target=tmp_path/"project/Assets/Resources/Town"
    assert (target/"Models/Tree.fbx").read_bytes()==b"mesh"
    assert not (target/"Legacy.cs").exists()
