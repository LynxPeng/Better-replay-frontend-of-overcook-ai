"""A Windows-produced app archive must retain executable bits on macOS."""
import importlib.util
import plistlib
import stat
import struct
from pathlib import Path
from zipfile import ZipFile

import pytest


def load_packager():
    spec = importlib.util.spec_from_file_location(
        "package_macos", Path(__file__).parents[1] / "scripts/package-macos.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def make_app(tmp_path):
    app = tmp_path / "OvercookedReplay.app"
    contents = app / "Contents"
    (contents / "MacOS").mkdir(parents=True)
    (contents / "Resources").mkdir()
    (contents / "Info.plist").write_bytes(plistlib.dumps({"CFBundleExecutable": "OvercookedReplay"}))
    # Valid architecture table with two minimal Mach-O slices.
    fat = struct.pack(">II", 0xCAFEBABE, 2)
    fat += struct.pack(">IIIII", 0x01000007, 3, 48, 32, 0)
    fat += struct.pack(">IIIII", 0x0100000C, 0, 80, 32, 0)
    fat += struct.pack("<IIIIIIII", 0xFEEDFACF, 0x01000007, 3, 2, 0, 0, 0, 0)
    fat += struct.pack("<IIIIIIII", 0xFEEDFACF, 0x0100000C, 0, 2, 0, 0, 0, 0)
    (contents / "MacOS/OvercookedReplay").write_bytes(fat)
    (contents / "Resources/中文.json").write_text("{}", encoding="utf-8")
    (contents / "Resources/not-a-library.dll").write_bytes(b"MZ managed assembly")
    return app


def test_archive_retains_executable_and_unicode_resources(tmp_path):
    module = load_packager()
    app = make_app(tmp_path)
    archive = tmp_path / "mac.zip"
    module.archive_app(app, archive)
    with ZipFile(archive) as bundle:
        executable = bundle.getinfo("OvercookedReplay.app/Contents/MacOS/OvercookedReplay")
        resource = bundle.getinfo("OvercookedReplay.app/Contents/Resources/中文.json")
        assert executable.create_system == 3
        assert stat.S_IMODE(executable.external_attr >> 16) == 0o755
        assert stat.S_IMODE(resource.external_attr >> 16) == 0o644
        assert bundle.read(resource) == b"{}"
    assert module.macho_architectures(app / "Contents/MacOS/OvercookedReplay") == {"x86_64", "arm64"}


def test_refuses_incomplete_app_without_creating_archive(tmp_path):
    module = load_packager()
    app = make_app(tmp_path)
    (app / "Contents/MacOS/OvercookedReplay").unlink()
    with pytest.raises(ValueError, match="executable"):
        module.archive_app(app, tmp_path / "mac.zip")
    assert not (tmp_path / "mac.zip").exists()


def test_refuses_to_overwrite_existing_archive(tmp_path):
    module = load_packager()
    archive = tmp_path / "mac.zip"
    archive.write_bytes(b"existing release")
    with pytest.raises(FileExistsError):
        module.archive_app(make_app(tmp_path), archive)
    assert archive.read_bytes() == b"existing release"
