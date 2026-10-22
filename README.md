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
- Declarations grant temporary/permanent actor traits, temporary/permanent equipment, membership-based additive stat bonuses, or **membership in another group** ("everyone in this kingdom speaks Latin").
- **Members** lists the creatures of the meta. **Add members** searches living creatures by name/ID and transfers one into the selected group, respecting requirements and game limits. Manual family/subspecies transfers require the same species; family transfers do not rewrite ancestry. Remove expels and bans; *Allow back* lifts the ban, then *Add* returns the creature immediately.
- **Mod settings** opens the native NML configuration. **Override cultural conversion blocks** is global, persistent and off by default. Enable it to let declarations/manual additions override `true_roots` (True Roots), without bypassing requirements, removals or structural game limits. Native conversions are unchanged. Disabling it does not undo completed transfers. **Replace conflicting traits** is on by default: a declared trait replaces held opposites (such as another mod's tier ladder) and a temporary grant hands them back when it ends. Off, conflicting grants are skipped.
- The editor refuses trait combinations that can never hold: opposite declared traits, a requirement needing opposites (or a trait and its negation) at once, or a declared trait that breaks the rule's own requirement. Such traits are faded in the picker.
- The header shows member counts, incompatibilities and the Active/Paused switch for the whole meta. The **?** page explains every behavior.

Examples:

- Imperial Guard: only members of the Sun clan; everyone receives Courageous, a silver sword and +10 armor.
- Northern Tongue: all speakers gain Cold-resistant while they speak it.
- House of Dawn: all members receive a permanent trait.

Trait selection includes traits registered by other mods. UI text is available in English and Brazilian Portuguese (`br`, also used for the game's `pt` language); any other game language falls back to English.

## Create a group while choosing a rule target

Click **+** in a group section of the picker to create an empty native language, culture, religion or clan. Requirement pickers also offer families and subspecies. An army's **+** shows its city; it is offered only for cities without an army, within the current kingdom when applicable. Each city keeps its native limit of one army. Creation remains accessible even when a search has no matches or no groups of that type exist.

The new meta starts with **zero members** and opens in the game's own details window. Use the native name field, banner customization and available trait tabs, then **Return to rule** (or native Back) to resume your draft with the new meta selected. Saving the rule remains a separate step. Canceling the rule does not delete a group you created.

Created metas use native IDs, managers, species defaults and world save data. Species follows the source group or equipment wearer where possible, with a human fallback for social groups that need naming templates. No founder is fabricated and nobody is transferred. The mod preserves these metas while empty, including after saving and loading; an army still depends on its city and kingdom. Cities and kingdoms must be founded through the game's world tools because they need territory.

## Rules on an individual equipment item

Open an item's native details window and select **Rules**. Declarations benefit its **wearer** with temporary/permanent traits, stat bonuses and membership grants. Requirements restrict who may wear it using traits, specific metas or another individual equipped item. Rules follow the unique item ID, not every copy of its asset. Meta requirements can also demand that exact equipment instance.

For example, a sword may require membership in the Sun clan and grant Courageous, +10 damage and entry into a particular religion. The editor uses **Wearers**, **While equipped** and an unequip impact preview in this context. Equipment condition choices show the name, ID and wearer/storage location; search narrows the first 200 results.

Stored items grant nothing. Unequipping, replacing, destroying or pausing a source removes its temporary effects on the next scan; permanent traits and granted memberships remain. Item and group trait sources share provenance, and stat bonuses stack. Equipment declarations offered by an item use other slots so they do not replace their own source. Group sources keep their precedence over equipment sources. Membership moves initiated by the mod also check the requirements of equipped items the creature keeps.

Incompatible wearers are unequipped on the next bounded scan, including cursed items. The item returns to city storage when available; configured ownerless items are preserved when there is no city. Native equipment transactions complete before the audit so loot and storage bookkeeping stay consistent. **Wearers → Unequip** removes the item immediately, even while paused, and also bans the creature from this item while rules are active; **Allow back** lifts that ban.

**Add wearer** hands this exact item, with or without rules, to any creature you choose (searchable, with an eligible-only filter). It leaves its current wearer or city storage; the new wearer's previous item in that slot goes to their city storage, or is lost if they have no city. Requirements, removals, cursed items and species that cannot use the item are respected. The new wearer's groups are checked against the traits they would hold once the item's grants settle, so a handover that would replace or override a trait one of their groups requires is refused and names that group.

The **+ Add members** shortcut now appears at the top of a meta's editor. Search by name/ID, select **Can join** to show eligible candidates, and hover unavailable Add buttons to see the reason. Successful additions identify the creature in the confirmation.

## Behavior

- Rules are embedded in the target meta's custom save data. Trait provenance is stored on affected actors. Each world has its own rules.
- Declarations apply to existing and future members, normally within a few seconds. Large worlds are processed in bounded batches.
- A temporary trait disappears when its last active source disappears. A trait that existed before the grant, or was independently granted later, is retained. Permanent grants are never automatically revoked.
- Conflicting traits are opposites or traits that remove one another when added (another tier, school or desire of the same kind, as `traits_to_remove`). When several sources declare conflicting traits, a worn item's trait wins over its wearer's groups while it is worn; otherwise the first group (army, clan, culture, religion, city, kingdom, language, family, subspecies) wins. The overridden trait returns when the winner's source ends. Unremovable conflicting traits are never forcibly removed; such a declaration stays unapplied until the conflict goes away.
- Equipment is served **once per membership**: the item replaces whatever the slot held (that item goes to the unit's city storage, as with a native upgrade) unless the slot is cursed. If the unit later upgrades, swaps or breaks it, the mod does not fight back or regenerate it. On leaving, a "while a member" item is removed only if the unit still carries that same item; permanent items stay. Only creatures that use items receive equipment, and when several groups declare the same slot the first one (army, clan, culture, religion, city, kingdom, language, family, subspecies) wins.
- Group grants (language, culture, religion, clan, kingdom, city, army) apply only when the game allows it (advanced creatures; clan not full and of the same species; no culture forbidding conversion; cities that welcome the creature, without moving kings or city leaders; armies only take residents of their own city, as warriors, within the army limit) and when neither the target's requirements nor those of any group the creature keeps would be broken. Changing city also changes kingdom and leaves the old army; the whole move is checked. Cities and kingdoms are applied before armies, a granted city wins over a granted kingdom, and otherwise the first group to grant a type wins. Leaving does not undo a grant. Families and subspecies cannot be granted.
- Removed individuals are stored per meta and count as failing its requirements while rules are enabled.
- Stat bonuses from different declarations add together; they do not accumulate every update. Supported stats: health, damage, armor, speed, attack speed, intelligence, warfare, diplomacy and stewardship. A health bonus changes capacity, not a direct heal.
- Requirements are enforced **continuously**, in the same bounded scan used for declarations. Incompatible existing members leave; losing a required trait or relation also triggers departure. In an army this includes the captain, and departing members are immediately removed from its displayed roster. Configured armies retain their rules if empty while their kingdom exists.
- City admission checks the city and its kingdom together. Expulsion from a city or kingdom uses native faction departure, ending army service and city citizenship and returning the unit to its default neutral/wild kingdom. Conquest first keeps political pointers consistent; eligibility is enforced during the next scan.
- A creature cannot have a null subspecies. An incompatible member transfers only to an eligible alternative of the same species. Without an alternative it remains, visibly counted as incompatible. Initial subspecies assignment is allowed so newborn creatures initialize correctly.
- Admission checks use current membership and traits; joining a group cannot qualify itself by the trait that group would grant afterward.
- Conditions reference traits, specific metas or equipped item instances. Membership declarations transfer actors only after checking destination, retained group and equipment requirements.

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

After building, the native lifecycle contracts can run against the original installed game assembly:

```sh
dotnet run --project Tests/NativeContracts -- "/path/to/worldbox/worldbox_Data/Managed" bin/Debug/netstandard2.1/MetaRules.dll
```

These contracts exercise all seven creation types' empty-object preservation, native-data JSON round trips, dead/unmarked cleanup, and army ownership dependencies. They do not start Unity or execute the creation factories and editors.

Engine tests cover individual meta/equipment identity, admission and ongoing eligibility, overlapping group/item trait sources, permanent grants, save/load, and additive stats. Local compilation has been checked against the installed game. **The new equipment and creation screens still need a visual playtest.** For creation, check each supported type, native banner/trait editing, both return buttons, canceling a draft, saving/reloading an empty group, and a city already at its army limit.

MIT. See [THIRD_PARTY_NOTICES](THIRD_PARTY_NOTICES) for UI attribution.
