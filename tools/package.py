#!/usr/bin/env python3
"""Build an installable, source-only NML archive."""
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
version = json.loads((ROOT / "mod.json").read_text())["version"]
output = ROOT / "dist" / f"MetaRules-v{version}.zip"
output.parent.mkdir(exist_ok=True)
files = [ROOT / name for name in ["mod.json", "default_config.json", "LICENSE", "THIRD_PARTY_NOTICES", "README.md", "README.pt-BR.md", "CHANGELOG.md", "icon.png"]]
files += sorted((ROOT / "Code").rglob("*.cs"))
files += sorted((ROOT / "Locales").glob("*.json"))
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for path in files:
        archive.write(path, "MetaRules/" + path.relative_to(ROOT).as_posix())
print(output)
