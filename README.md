# Meta Rules for WorldBox

![Meta Rules](metaRules.png)

[Português (Brasil)](README.pt-BR.md)

Define who may join **one particular group**, and what its members receive. Rules belong to individual armies, clans, cultures, religions, cities, kingdoms, languages, families and subspecies—not to every group of the same type.

Built for WorldBox 0.51.2 / build 719 with [NeoModLoader](https://github.com/WorldBoxOpenMods/ModLoader). The UI uses native windows and tabs, adapting MIT-licensed helpers from [Diplomacy Overhaul](https://github.com/Lapis0x0/worldbox-diplomacy-overhaul).

## Open the editor

Open the normal details window for a specific meta, then click its **Rules** tab (the options icon). The editor opens for that exact instance, showing its own banner and name. There is no global meta browser or type selection step.

- **Declarations** and **Requirements** are visible together, each with one add button.
- Rules use native trait, item and stat icons and each meta's real banner. Hover for names and explanations; click a symbol to edit it. ALL / ANY, duration and enable/disable are visible switches in each rule editor.
- The window has the size of the game's own editors and uses their scroll view, scrollbar, trait/item buttons and meta banners. The picker follows the native trait and equipment editors (same groups, colors and order) and scrolls continuously; typing filters it.
- Requirements can demand traits or membership in another specific meta. All enabled requirements must pass, for existing members as well as new admissions.
- Declarations grant temporary/permanent actor traits, temporary/permanent equipment, or membership-based additive stat bonuses.
- The header shows member counts, incompatibilities and the Active/Paused switch for the whole meta. The **?** page explains every behavior.

Examples:

- Imperial Guard: only members of the Sun clan; everyone receives Courageous, a silver sword and +10 armor.
- Northern Tongue: all speakers gain Cold-resistant while they speak it.
- House of Dawn: all members receive a permanent trait.

Trait selection includes traits registered by other mods. UI text is available in English and Brazilian Portuguese (`br`, also used for the game's `pt` language); any other game language falls back to English.

## Behavior

- Rules are embedded in the target meta's custom save data. Trait provenance is stored on affected actors. Each world has its own rules.
- Declarations apply to existing and future members, normally within a few seconds. Large worlds are processed in bounded batches.
- A temporary trait disappears when its last active source disappears. A trait that existed before the grant, or was independently granted later, is retained. Permanent grants are never automatically revoked.
- Opposite traits are not forcibly removed. A conflicting declaration may remain unapplied until the conflict goes away.
- Equipment is served **once per membership**: the item replaces whatever the slot held (that item goes to the unit's city storage, as with a native upgrade) unless the slot is cursed. If the unit later upgrades, swaps or breaks it, the mod does not fight back or regenerate it. On leaving, a "while a member" item is removed only if the unit still carries that same item; permanent items stay. Only creatures that use items receive equipment, and when several groups declare the same slot the first one (army, clan, culture, religion, city, kingdom, language, family, subspecies) wins.
- Stat bonuses from different declarations add together; they do not accumulate every update. Supported stats: health, damage, armor, speed, attack speed, intelligence, warfare, diplomacy and stewardship. A health bonus changes capacity, not a direct heal.
- Requirements are enforced **continuously**, in the same bounded scan used for declarations. Incompatible existing members leave; losing a required trait or relation also triggers departure. In an army this includes the captain, and departing members are immediately removed from its displayed roster. Configured armies retain their rules if empty while their kingdom exists.
- City admission checks the city and its kingdom together. Expulsion from a city or kingdom uses native faction departure, ending army service and city citizenship and returning the unit to its default neutral/wild kingdom. Conquest first keeps political pointers consistent; eligibility is enforced during the next scan.
- A creature cannot have a null subspecies. An incompatible member transfers only to an eligible alternative of the same species. Without an alternative it remains, visibly counted as incompatible. Initial subspecies assignment is allowed so newborn creatures initialize correctly.
- Admission checks use current membership and traits; joining a group cannot qualify itself by the trait that group would grant afterward.
- Conditions reference another meta; declarations grant actor traits, equipment and stats. They do not automatically transfer actors between clans, cultures or other metas.

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

Engine tests cover individual meta identity, admission/continued-membership decisions, overlapping trait sources, permanent grants, provenance after save/load, and additive stats. Local compilation has been checked against the installed game. Version 0.1.0 was confirmed loaded in the game log; **the 0.3.0 interface and equipment declarations still need a playtest.**

MIT. See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES) for UI attribution.
