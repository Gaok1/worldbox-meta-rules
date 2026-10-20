# Changelog

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
