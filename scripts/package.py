"""LGC VRC Quick Login v1.1.3: build a credential-free, allowlisted VPM ZIP."""
import hashlib
import json
import os
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = (
    "package.json", "package.json.meta", "README.md", "README.md.meta",
    "CHANGELOG.md", "CHANGELOG.md.meta", "LICENSE.md", "LICENSE.md.meta",
    "Editor.meta", "Editor/LGC.VrcQuickLogin.Editor.asmdef",
    "Editor/LGC.VrcQuickLogin.Editor.asmdef.meta",
    "Editor/VrcQuickLoginWindow.cs", "Editor/VrcQuickLoginWindow.cs.meta",
)


def build():
    manifest = json.loads((ROOT / "package.json").read_text(encoding="utf-8-sig"))
    name, version = manifest["name"], manifest["version"]
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("A stable semantic package version is required")
    script = (ROOT / "Editor/VrcQuickLoginWindow.cs").read_text(encoding="utf-8-sig")
    readme = (ROOT / "README.md").read_text(encoding="utf-8-sig")
    changelog = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8-sig")
    assert f'Version = "{version}"' in script, "Window version mismatch"
    assert f"v{version}" in script.splitlines()[0], "Script header version mismatch"
    assert f"v{version}" in readme.splitlines()[0], "README version mismatch"
    assert changelog.split("## ", 1)[1].startswith(f"v{version} - "), "Changelog version mismatch"
    assert "Windows" in readme
    expected_url = f"https://github.com/LgcChina/VRCQuickLogin/releases/download/v{version}/{name}-{version}.zip"
    assert manifest["url"] == expected_url, "Download URL mismatch"
    assembly = json.loads((ROOT / "Editor/LGC.VrcQuickLogin.Editor.asmdef").read_text(encoding="utf-8-sig"))
    assert assembly["includePlatforms"] == ["Editor"], "Assembly must remain Editor-only"
    assert "#if UNITY_EDITOR" in script
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    archive = output / f"{name}-{version}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
        for relative in FILES:
            data = (ROOT / relative).read_bytes().replace(b"\r\n", b"\n")
            entry = zipfile.ZipInfo(relative, date_time=(2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            package.writestr(entry, data)
    with zipfile.ZipFile(archive) as package:
        assert set(package.namelist()) == set(FILES)
        assert package.testzip() is None
        assert json.loads(package.read("package.json"))["version"] == version
    (output / "package.json").write_bytes((ROOT / "package.json").read_bytes().replace(b"\r\n", b"\n"))
    notes = changelog.split("## ", 1)[1].split("\n## ", 1)[0].strip()
    (output / "release-notes.md").write_text(notes + "\n", encoding="utf-8")
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    print(f"Built {archive.name}: {len(FILES)} allowlisted files; SHA256 {digest}")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output_file:
            output_file.write(f"tag=v{version}\nname={name}\nversion={version}\nzip={archive.name}\n")


if __name__ == "__main__":
    build()
