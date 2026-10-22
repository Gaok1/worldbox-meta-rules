# Changelog

## 0.8.0

- See why a rule does not apply. Declarations that do not reach every member get a colored dot and a **Not applying** row with their count and main reason (a requirement and its condition, a game limit, a ban, a cursed item, another source that wins, a trait changed later by the game or another mod). The declaration editor shows a **Status** card with a bar split by reason and previews unsaved changes; the members and wearers lists mark the creatures a declaration skips, and refused additions say exactly why.
- A worn item's group grants and declared equipment now win over its wearer's groups while worn, like its traits (e.g. a ring granting a language over the species' language).
- Traits that remove one another on addition (`traits_to_remove`: another tier, school or desire of the same kind) count as conflicting, in the editor and when granting. Previously such grants silently removed each other and, when two sources declared them, alternated on every scan.
- Conflicting declared traits from several sources settle instead of alternating: a worn item's trait overrides its wearer's groups while worn (e.g. a sword teaching a sword school over the bare-handed school a species teaches); otherwise the first group wins. The overridden trait returns when the item leaves. Adding a wearer reports which group traits the item overrides.
- Add wearer and membership grants check the creature's kept groups against the traits it would hold once the new item's or group's grants settle. A handover that would replace or override a trait one of its groups requires is refused, naming that group.
- Declared traits replace held opposites, like the native trait editor (e.g. another mod's tier ladder: a Novice wielding a sword that grants Grandmaster now becomes Grandmaster). A temporary grant hands the replaced traits back when it ends, unless something newer took their place; a later change by the game or another mod is never overruled. Unremovable opposites still block the grant. A new **Replace conflicting traits** setting (on by default) turns this off.
- The editor refuses conflicting traits: opposite declarations, a requirement needing opposites or a trait and its negation at once, and a declaration that breaks the rule's own requirement. Conflicting traits are faded in the picker and explained on click; saving is blocked with the reason.
- Add wearer: hand any item to a chosen creature from its rules window. The item leaves its current wearer or city storage, and the new wearer's previous item goes to their city. Requirements, removals, cursed items and species equipment limits are respected.

## 0.7.0

- Create empty native languages, cultures, religions and clans directly from declaration pickers; requirement pickers also offer families and subspecies. An army can be created for a city without one, respecting the native one-army limit. Nobody is assigned during creation.
- Open the game's own meta window to edit names, banners, colors and available traits. Return to rule or use native Back to continue the existing draft with the new meta selected. Creation entries stay available during search, including when no metas exist yet.
- Preserve editor-created empty metas through native custom save data, including paused saves. Other groups retain native cleanup; armies still require a surviving city and kingdom. Cities and kingdoms must be founded with the game's territory tools.

## 0.6.0

- Add Rules to individual equipment windows: wearer declarations for traits, stats and meta grants, plus requirements that unequip incompatible wearers. Rules persist on the item; temporary effects follow actual equipment use and share provenance with group effects. Configured ownerless items are preserved.
- Requirements can reference an exact equipped item. Equipment choices show name, ID and wearer/storage location, with searchable bounded results.
- Put Add members at the top of the editor, add an eligible-candidate filter, clarify blocked additions and identify successful additions. Use equipment-specific labels and impact previews; size long grant previews and feedback to fit.
- Cover equipment identity, admission, mixed sources, permanent effects, pause/resume, save/load and retained equipment requirements in engine tests.

## 0.5.0

- Add living creatures from the Members page, with name/ID search and revalidated admission checks. All nine meta types are supported; manual family/subspecies transfers stay within the same species, and family transfers do not rewrite ancestry. Requirements, bans and structural limits remain enforced. Allow back lifts a removal; Add performs the actual transfer.
- Add a persistent global NML setting to override cultural conversion blocks such as `true_roots` for declarations and manual additions. Off by default; native conversions and configured requirements remain unchanged. Includes a Mod settings shortcut in the rules window. Disabling the setting does not undo completed transfers.

## 0.4.0

- Declare group memberships: "everyone in this kingdom speaks Latin", "every clan member serves in this army and lives in that city". Members are made to join a specific language, culture, religion, clan, kingdom, city or army, only when the game allows it (advanced creatures; clan capacity and species; cultures that forbid conversion; cities that welcome the creature; kings and city leaders stay; armies take residents of their city as warriors within the limit) and when no requirement of the target or of any group they keep would be broken. Moves that change several memberships (city → kingdom, leaving the old army) are checked as a whole. Cities and armies are listed for the selected meta's kingdom, or grouped per kingdom. Families and subspecies cannot be granted.
- Manage individuals: a Members page lists the meta's creatures (searchable, click to open one). Removing an individual expels it and keeps it out while rules are active; removals can be undone with Allow back.
- Impact previews before saving: a requirement shows how many current members would leave (red, with a second confirmation click, when it would empty the group); a group grant shows how many members would switch; the Active/Paused tooltip shows how many would leave on resume.
- Declaration picker tabs are icons (Traits, Equipment, Stats, Groups) with the active one named in the title.

## 0.3.0

- Declare equipment. Each member receives the item once per membership, in its own slot (the replaced item goes to the city's storage). "While a member" items are removed on leaving if still carried; permanent items are kept. Later swaps, upgrades or breakage are respected, never regenerated.
- The trait picker follows the native trait editor: grouped by category with native colors, ordered by priority. Equipment follows the native equipment editor.
- Picker results scroll continuously with group headers instead of pages; searching filters in place without losing focus.
- Every meta type shows its real banner: subspecies (with creature), families, cultures, kingdoms, cities and armies were previously generic icons.
- Native window: same size as the game's editors, one continuous scroll view with the native scrollbar. Groups, trait and item buttons and meta banners are the game's own prefabs (native tooltips, sizes and selection marks).
- Header with banner, member count, incompatibilities and an Active/Paused switch; labeled tabs; visible ALL/ANY, duration and enabled controls; Cancel/Save/Remove footer; stat steppers; a help page.
- Languages: English and Brazilian Portuguese; European Portuguese (`pt`) uses the pt-BR text, every other language falls back to English.

## 0.2.0

- Replace the global browser and three-tab editor with one individual meta's banner/name and two visible sections: Declarations and Requirements.
- Show native trait icons and actual meta banners, with tooltips and click-to-edit controls.
- Keep search, category filters, deletion, enable/disable and condition logic inside context-specific editors.
- Preserve the selection when returning to the native meta window; keep existing save data and rule IDs.
- Enforce requirements against existing members, not just future admissions. Remove ineligible army members/captains and clean up their temporary grants.
- Preserve configured empty armies while their kingdom exists.
- Handle city/kingdom departure through native faction methods; require a same-species alternative for subspecies transfer.
- Keep icon grids scrollable while hovering a symbol.

## 0.1.0

- Initial individual-meta rules, native window entry points and English/pt-BR localization.
