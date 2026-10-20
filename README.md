# Meta Rules for WorldBox

[Português (Brasil)](README.pt-BR.md)

Define who may join **one particular group**, and what its members receive. Rules belong to individual armies, clans, cultures, religions, cities, kingdoms, languages, families and subspecies—not to every group of the same type.

Built for WorldBox 0.51.2 / build 719 with [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader). The UI uses native windows and tabs, adapting MIT-licensed helpers from [Diplomacy Overhaul](https://github.com/Lapis0x0/worldbox-diplomacy-overhaul).

## Open the editor

Open the normal details window for a specific meta, then click its **Meta Rules** tab (the options icon). The editor opens for that exact instance. You can also browse individual metas from the Meta Rules power tab.

- **Restrict:** require traits or membership in another specific meta. Combine conditions with ALL / ANY and optionally NOT. All enabled restrictions must pass.
- **Declare:** give members a trait, temporarily or permanently, or add a stat bonus for the duration of membership.
- **Overview:** see member counts, existing members who do not meet admission conditions, and enable/disable this meta's rules.

Examples:

- Imperial Guard: only members of the Sun clan; everyone receives Courageous and +10 armor.
- Northern Tongue: all speakers gain Cold-resistant while they speak it.
- House of Dawn: all members receive a permanent trait.

Trait selection includes traits registered by other mods. UI text is available in English and Brazilian Portuguese (`br`).

## Behavior

- Rules are embedded in the target meta's custom save data. Trait provenance is stored on affected actors. Each world has its own rules.
- Declarations apply to existing and future members, normally within a few seconds. Large worlds are processed in bounded batches.
- A temporary trait disappears when its last active source disappears. A trait that existed before the grant, or was independently granted later, is retained. Permanent grants are never automatically revoked.
- Opposite traits are not forcibly removed. A conflicting declaration may remain unapplied until the conflict goes away.
- Stat bonuses from different declarations add together; they do not accumulate every update. Supported stats: health, damage, armor, speed, attack speed, intelligence, warfare, diplomacy and stewardship. A health bonus changes capacity, not a direct heal.
- Restrictions govern **future admissions**. Existing members remain; the overview reports their violations. There is no automatic expulsion.
- City admission checks the city and its kingdom together. Conquest-induced kingdom changes are allowed so residents never end up belonging to a different kingdom from their city. Initial subspecies assignment is also allowed so newborn creatures can initialize correctly. Both cases remain visible to membership auditing.
- Admission checks use current membership and traits; joining a group cannot qualify itself by the trait that group would grant afterward.
- Conditions reference another meta; declarations currently grant actor traits and stats. They do not automatically transfer actors between clans, cultures or other metas.

## Install

Download the source package and extract `MetaRules/` into `worldbox/Mods/`, with `mod.json` directly inside that folder. Restart the game so NeoModLoader can compile and load it. Do not install a second copy alongside a Workshop installation.

For a development checkout, symlink this directory to `worldbox/Mods/MetaRules` or copy `Code`, `Locales`, `mod.json`, `LICENSE` and `THIRD_PARTY_NOTICES` there.

## Develop and check

Requires .NET SDK 8 and Python 3. No NuGet packages are needed by the engine tests.

```sh
dotnet run --project Tests/MetaRules.Tests.csproj
python3 tools/check.py
dotnet build -p:GameData="/path/to/worldbox/worldbox_Data"
python3 tools/package.py
```

On Linux the project defaults to `~/.local/share/Steam/steamapps/common/worldbox/worldbox_Data`. Build references use the game's installed assemblies and NML publicized assembly; proprietary game binaries are not distributed.

Engine tests cover individual meta identity, admission logic, overlapping trait sources, permanent grants, provenance after save/load, and additive stats. Local compilation has been checked against the installed game. **In-game loading, tab placement and interaction with Frontiers/Diplomacy Overhaul still need a playtest.**

MIT. See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES) for UI attribution.
