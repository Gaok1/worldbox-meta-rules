#!/usr/bin/env python3
"""Validate translation coverage, metadata and distributable paths."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def unique(pairs):
    data = {}
    for key, value in pairs:
        assert key not in data, f"Duplicate JSON key: {key}"
        data[key] = value
    return data

def load(path):
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)

metadata = load(ROOT / "mod.json")
assert metadata["GUID"] == "gaok1.meta_rules"
assert re.fullmatch(r"\d+\.\d+\.\d+", metadata["version"])
assert (ROOT / metadata["iconPath"]).is_file(), "Mod icon missing"
en = load(ROOT / "Locales/en.json")
br = load(ROOT / "Locales/br.json")
# The game has separate Brazilian ("br") and European ("pt") Portuguese; both use the pt-BR text.
# Any other game language falls back to English through NML.
pt = load(ROOT / "Locales/pt.json")
assert en.keys() == br.keys(), "Locale keys differ"
assert pt == br, "Locales/pt.json must be a copy of Locales/br.json"
config = load(ROOT / "default_config.json")
option = config["mr_settings"][0]
assert option["Id"] == "mr_override_cultural_block" and option["Type"] == "SWITCH"
assert option["BoolVal"] is False, "Existing behavior must remain the default"
assert "mr_settings" in en and option["Id"] in en and option["Id"] + " Description" in en
assert '"default_config.json"' in (ROOT / "tools/package.py").read_text(), "Package must include settings"
assert all(isinstance(value, str) and value.strip() for value in [*en.values(), *br.values()])
keys = set()
for path in (ROOT / "Code").rglob("*.cs"):
    keys.update("mr_" + key for key in re.findall(r'\bT\("([a-z_]+)"\)', path.read_text(encoding="utf-8")))
# Prefixes are completed dynamically by enum/stat selectors.
keys -= {"mr_stat_", "mr_kind_"}
assert keys <= en.keys(), f"Missing translations: {keys - en.keys()}"
for key in ["declarations", "requirements", "add_declaration", "add_requirement", "open", "open_description", "title"]:
    assert "mr_" + key in en
assert not list((ROOT / "Code").rglob("*.dll")), "Game binaries must not be distributed"
print(f"PASS: metadata, {len(en)} English/pt-BR entries and source-only distribution")
