using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MetaRules.Core;
using MetaRules.Game;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static MetaRules.UI.UiKit;
using Object = UnityEngine.Object;

namespace MetaRules.UI
{
    // One individual meta in a native-sized window: everything lives in the window's own
    // scroll view (with its native scrollbar), built from the game's own prefabs.
    public static class RulesWindow
    {
        const string WindowId = "mr_window";
        // Native window geometry (windows/empty): 240x320 background, 232-wide scroll view.
        const float CW = 210f, Left = 15f, ViewW = 232f, ViewH = 286.2f, ViewY = -6.5f, ViewTop = 23.4f, PickerInset = 64f;
        const float Cell = NativeUi.CellSize;
        const string Red = "ui/special/special_buttonRed", Gray = "ui/special/special_buttonGray", Field = "ui/special/darkInputFieldEmpty";
        static readonly Color Good = Hex("#8fd16a"), Bad = Hex("#ef7a62");
        static ScrollWindow window;
        static RectTransform root, content, scrollView;
        static MetaHandle selected;
        static MapStats world;
        static Restriction draft;
        static Declaration effect;
        static int selectedCondition = -1;
        static bool picking, pickingCondition, help;
        static int category;
        static string query = "";
        static Text summaryLabel;
        static int memberCount, incompatibleCount;
        static float nextSummary;
        static List<Group> results;

        sealed class Choice
        {
            public string Search;
            public Func<Transform, RectTransform> Create;
            public RectTransform View;
        }

        sealed class Group
        {
            public string Title;
            public Color Color = HeaderGold;
            public bool Banners;
            public readonly List<Choice> Items = new List<Choice>();
            public NativeUi.Group View;
        }

        public static string T(string key) => LM.Get("mr_" + key);
        public static string KindName(MetaKind value) => T("kind_" + value.ToString().ToLowerInvariant());
        static string Locale(string key, string fallback) => !string.IsNullOrEmpty(key) && LocalizedTextManager.stringExists(key) ? LocalizedTextManager.getText(key) : fallback;
        static string TraitName(string id) => Locale("trait_" + id, id);
        static RuleSet Current => WorldRules.Get(selected) ?? new RuleSet();

        static string ItemName(string id)
        {
            EquipmentAsset asset = AssetManager.items.get(id);
            if (asset == null) return id;
            try { return asset.getTranslatedName(); } catch (Exception) { return id; }
        }

        static string SlotName(string id)
        {
            EquipmentAsset asset = AssetManager.items.get(id);
            return asset == null ? T("equipment") : T("slot_" + asset.equipment_type.ToString().ToLowerInvariant());
        }

        // Entry points are attached to native meta windows, not a global type browser.
        public static void Init() { }

        public static void Tick()
        {
            if (window == null || !window.gameObject.activeInHierarchy || summaryLabel == null || selected == null ||
                Time.realtimeSinceStartup < nextSummary || !ReferenceEquals(world, World.world?.map_stats) || SmoothLoader.isLoading()) return;
            nextSummary = Time.realtimeSinceStartup + 1f;
            UpdateSummary();
        }

        public static void OpenFor(MetaHandle meta)
        {
            if (meta == null || World.world?.units == null || SmoothLoader.isLoading()) return;
            world = World.world.map_stats;
            WorldRules.RefreshMetas();
            selected = meta;
            ResetEditor();
            if (window == null) Create();
            ScrollWindow.showWindow(WindowId);
            Build();
        }

        static void ResetEditor()
        {
            draft = null; effect = null; selectedCondition = -1;
            picking = false; help = false; query = "";
        }

        static void Create()
        {
            window = WindowCreator.CreateEmptyWindow(WindowId, "mr_title");
            ScrollWindow.finishAnimations();
            Transform bg = window.transform.Find("Background");

            // Undo NML's list-window offsets and restore the native scroll view.
            scrollView = (RectTransform)bg.Find("Scroll View");
            scrollView.gameObject.SetActive(true);
            scrollView.anchorMin = scrollView.anchorMax = scrollView.pivot = new Vector2(0.5f, 0.5f);
            var viewport = (RectTransform)scrollView.Find("Viewport");
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.pivot = new Vector2(0f, 1f);
            viewport.sizeDelta = Vector2.zero; viewport.anchoredPosition = Vector2.zero;
            content = (RectTransform)viewport.Find("Content");
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(0f, 1f);
            VerticalLayoutGroup stack = content.GetComponent<VerticalLayoutGroup>();
            if (stack == null) stack = content.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.spacing = 4f;
            stack.padding = new RectOffset(0, 0, 4, 10);
            stack.childControlWidth = stack.childControlHeight = false;
            stack.childForceExpandWidth = stack.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scrollView.Find("Scrollbar Horizontal")?.gameObject.SetActive(false);

            // The native vertical scrollbar covers only part of the view in list windows; span it fully.
            if (scrollView.Find("Scrollbar Vertical Mask") is RectTransform mask)
            {
                mask.anchorMin = new Vector2(1f, 0f); mask.anchorMax = Vector2.one; mask.pivot = new Vector2(0.5f, 0.5f);
                mask.sizeDelta = new Vector2(17.4f, -8f); mask.anchoredPosition = new Vector2(8.7f, 0f);
                if (mask.Find("Scrollbar Vertical") is RectTransform bar)
                {
                    bar.anchorMin = Vector2.zero; bar.anchorMax = Vector2.one;
                    bar.sizeDelta = new Vector2(-0.2f, 0f); bar.anchoredPosition = Vector2.zero;
                    if (bar.Find("Background") is RectTransform track)
                    {
                        track.anchorMin = new Vector2(0.5f, 0f); track.anchorMax = new Vector2(0.5f, 1f);
                        track.sizeDelta = new Vector2(36f, 0f); track.anchoredPosition = new Vector2(-4.3f, 0f);
                    }
                }
            }

            // Fixed controls (picker tabs and search) sit above the scroll view.
            var size = ((RectTransform)bg).sizeDelta;
            root = Box(bg, 0, 0, size.x, size.y, "mr_root");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.SetAsLastSibling();
        }

        static void SetInset(float top)
        {
            scrollView.sizeDelta = new Vector2(ViewW, ViewH - top);
            scrollView.anchoredPosition = new Vector2(0f, ViewY - top / 2f);
        }

        static void Clear(Transform parent)
        {
            foreach (Transform child in parent)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        static RectTransform Block(float height, string name = "block") => Box(content, 0, 0, CW, height, name);

        static void Build()
        {
            if (root == null) return;
            Tooltip.hideTooltip();
            Clear(root);
            Clear(content);
            results = null;
            summaryLabel = null;
            SetInset(picking ? PickerInset : 0f);
            if (!ReferenceEquals(world, World.world?.map_stats) || selected == null ||
                !WorldRules.Metas.Any(m => m.Target == selected.Target && m.Id == selected.Id))
                Paragraph(Block(80), T("choose_meta"), 0, 10, CW, 70);
            else if (picking) BuildPicker();
            else
            {
                BuildIdentity();
                if (WorldRules.IsBroken(selected)) Paragraph(Block(90), T("invalid_data"), 0, 0, CW, 90);
                else if (help) BuildHelp();
                else if (draft != null) BuildRestrictionEditor();
                else if (effect != null) BuildDeclarationEditor();
                else BuildOverview();
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = Vector2.zero;
        }

        // ── Header: who these rules belong to, how many members, and the master switch.
        static void BuildIdentity()
        {
            RuleSet set = Current;
            RectTransform block = Block(48, "identity");
            RectTransform banner = NativeUi.Banner(block, selected, 40, ReturnToMeta);
            banner.anchoredPosition = new Vector2(0, -4);
            Text(block, Fit(selected.Name, 10, CW - 48), 46, 1, CW - 46, 16, 10, Ink, TextAnchor.MiddleLeft);
            summaryLabel = Text(block, "", 46, 16, CW - 46, 12, 7, Muted, TextAnchor.MiddleLeft).GetComponent<Text>();
            RectTransform hover = Button(block, 46, 16, CW - 46, 12, null, () => { });
            MetaSymbols.Tip(hover, () => T("members") + ": " + memberCount,
                () => T("violations") + ": " + incompatibleCount + "\n\n" + T("admission_help") +
                    (selected.Kind == MetaKind.Subspecies ? "\n\n" + T("subspecies_help") : ""));
            UpdateSummary();

            RectTransform toggle = Button(block, 46, 30, 90, 17, Gray, () => { set.Enabled = !set.Enabled; Save(set); });
            Text(toggle, "● " + T(set.Enabled ? "active" : "paused"), 2, 0, 86, 17, 7, set.Enabled ? Good : Bad, TextAnchor.MiddleCenter);
            MetaSymbols.Tip(toggle, () => T(set.Enabled ? "rules_on" : "rules_off"), () => T("toggle_rules"));
            Small(block, "?", CW - 18, 30, 18, () => { ResetEditor(); help = true; Build(); }, T("help"));
        }

        static void ReturnToMeta() => NativeMetaWindows.OpenNative(selected);

        static void UpdateSummary()
        {
            if (summaryLabel == null) return;
            memberCount = WorldRules.MemberCount(selected, out incompatibleCount);
            summaryLabel.text = KindName(selected.Kind) + " · " + memberCount + " " + T("members").ToLowerInvariant() +
                (incompatibleCount > 0 ? " · <color=" + Tag(Bad) + ">" + incompatibleCount + " " + T("incompatible") + "</color>" : "");
        }

        // ── Overview
        static NativeUi.Group Section(string title, int count)
        {
            NativeUi.Group group = NativeUi.CreateGroup(content, title, HeaderGold);
            if (group.Counter != null) group.Counter.text = count.ToString();
            return group;
        }

        static void BuildOverview()
        {
            RuleSet set = Current;
            NativeUi.Group declarations = Section(T("declarations"), set.Declarations.Count);
            foreach (Declaration value in set.Declarations)
            {
                Declaration item = value;
                RectTransform token = DeclarationToken(declarations.Grid, item, () => { effect = Clone(item); Build(); }, false);
                if (!item.Enabled || !set.Enabled) Fade(token);
                if (item.Kind == EffectKind.Stat) Badge(token, Number(item.Amount));
                else if (item.Lifetime == Lifetime.Permanent) Corner(token, Gold);
            }
            Small(declarations.Grid, "+", 0, 0, Cell, () => { effect = new Declaration(); StartPicker(false); }, T("add_declaration"));
            if (set.Declarations.Count == 0) Hint(T("no_declarations") + " — " + T("declarations_hint"));

            NativeUi.Group requirements = Section(T("requirements"), set.Restrictions.Count);
            float rowW = NativeUi.MakeList(requirements, 2f);
            foreach (Restriction value in set.Restrictions)
            {
                Restriction item = value;
                RectTransform row = Button(requirements.Grid, 0, 0, rowW, 24, Slot, () => { draft = Clone(item); selectedCondition = -1; Build(); });
                Text(row, T(item.Any ? "any_short" : "all_short"), 2, 0, 32, 24, 6, Gold, TextAnchor.MiddleCenter);
                int fit = (int)((rowW - 36 - (item.Enabled ? 4 : 44)) / 20);
                int shown = Math.Min(item.Conditions.Count, item.Conditions.Count > fit ? fit - 1 : fit);
                for (int j = 0; j < shown; j++) DrawCondition(row, item.Conditions[j], 36 + j * 20, 3, 18);
                if (item.Conditions.Count > shown) Text(row, "+" + (item.Conditions.Count - shown), 36 + shown * 20, 0, 20, 24, 7, Muted, TextAnchor.MiddleCenter);
                if (!item.Enabled) Text(row, T("disabled"), rowW - 44, 0, 42, 24, 6, Bad, TextAnchor.MiddleCenter);
                MetaSymbols.Tip(row, () => T(item.Any ? "any" : "all"), () => string.Join("\n", item.Conditions.Select(Describe)));
                if (!item.Enabled || !set.Enabled) Fade(row);
            }
            Label(requirements.Grid, "+  " + T("add_requirement"), 0, 0, rowW, 18, () => { draft = new Restriction(); StartPicker(true); });
            if (set.Restrictions.Count == 0) Hint(T("no_requirements") + " — " + T("requirements_hint"));
        }

        static RectTransform DeclarationToken(Transform parent, Declaration d, Action click, bool marked)
        {
            RectTransform token = null;
            if (d.Kind == EffectKind.Trait && AssetManager.traits.get(d.Value) is ActorTrait trait)
                token = NativeUi.TraitButton(parent, trait, marked, click);
            else if (d.Kind == EffectKind.Item && AssetManager.items.get(d.Value) is EquipmentAsset item)
                token = NativeUi.ItemButton(parent, item, marked, click);
            return token ?? Symbol(parent, 0, 0, Cell, DeclarationSprite(d), null, click, DeclarationName(d), KindLabel(d));
        }

        static void Fade(RectTransform target)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null) group = target.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0.4f;
        }

        static string Number(float n) => n.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);

        static Sprite DeclarationSprite(Declaration d)
        {
            switch (d.Kind)
            {
                case EffectKind.Trait: return MetaSymbols.TraitSprite(d.Value);
                case EffectKind.Item: return MetaSymbols.ItemSprite(d.Value);
                default: return MetaSymbols.StatSprite(d.Value);
            }
        }

        static string DeclarationName(Declaration d)
        {
            switch (d.Kind)
            {
                case EffectKind.Trait: return TraitName(d.Value);
                case EffectKind.Item: return ItemName(d.Value);
                default: return T("stat_" + d.Value) + " " + Number(d.Amount);
            }
        }

        static string KindLabel(Declaration d)
        {
            switch (d.Kind)
            {
                case EffectKind.Trait: return T("trait");
                case EffectKind.Item: return T("equipment") + " · " + SlotName(d.Value);
                default: return T("stat");
            }
        }

        static Declaration Clone(Declaration d) => new Declaration { Id = d.Id, Enabled = d.Enabled, Kind = d.Kind, Value = d.Value, Amount = d.Amount, Lifetime = d.Lifetime };
        static Restriction Clone(Restriction r) => new Restriction
        {
            Id = r.Id, Enabled = r.Enabled, Any = r.Any,
            Conditions = r.Conditions.Select(c => new Condition { Kind = c.Kind, Trait = c.Trait, Meta = c.Meta, MetaId = c.MetaId, Negated = c.Negated }).ToList()
        };
        static MetaHandle Referenced(Condition c) => WorldRules.Metas.FirstOrDefault(m => m.Kind == c.Meta && m.Id == c.MetaId);
        static string Describe(Condition c) => (c.Negated ? T("not") + " " : "") +
            (c.Kind == ConditionKind.Trait ? TraitName(c.Trait) : KindName(c.Meta) + ": " + (Referenced(c)?.Name ?? T("missing_meta")));

        static void DrawCondition(Transform parent, Condition c, float x, float y, float size)
        {
            if (c.Kind == ConditionKind.Trait) Picture(parent, MetaSymbols.TraitSprite(c.Trait), x + 1, y + 1, size - 2, size - 2);
            else if (Referenced(c) is MetaHandle meta) MetaSymbols.Draw(parent, meta, x, y, size);
            else Picture(parent, MetaSymbols.KindSprite(c.Meta), x + 1, y + 1, size - 2, size - 2);
            if (c.Negated)
            {
                RectTransform slash = Rect(parent, x, y + size / 2, size * 1.2f, 2, Bad);
                slash.pivot = new Vector2(0.5f, 0.5f);
                slash.anchoredPosition = new Vector2(x + size / 2, -(y + size / 2));
                slash.localRotation = Quaternion.Euler(0, 0, 45);
            }
        }

        // ── Editors
        static void EditorHeader(Transform parent, string title, Action back)
        {
            Small(parent, "‹", 0, 0, 18, back, T("back"));
            Text(parent, Fit(title, 9, CW - 24), 24, 0, CW - 24, 18, 9, HeaderGold, TextAnchor.MiddleLeft);
        }

        static void Row(Transform parent, string label, float y) =>
            Text(parent, Fit(label, 7, 58), 0, y, 60, 18, 7, Muted, TextAnchor.MiddleLeft);

        static void BuildDeclarationEditor()
        {
            bool exists = Current.Declarations.Any(d => d.Id == effect.Id);
            RectTransform block = Block(144, "declaration");
            EditorHeader(block, T(exists ? "declaration" : "add_declaration"), () => { effect = null; Build(); });

            RectTransform card = Sliced(block, Slot, 0, 22, CW, 38);
            RectTransform preview = DeclarationToken(card, effect, () => StartPicker(false), false);
            preview.anchorMin = preview.anchorMax = new Vector2(0f, 1f);
            preview.pivot = new Vector2(0.5f, 0.5f);
            preview.anchoredPosition = new Vector2(5 + Cell / 2, -19);
            string title = effect.Kind == EffectKind.Stat ? T("stat_" + effect.Value) : DeclarationName(effect);
            Text(card, Fit(title, 8, CW - 104), 38, 5, CW - 104, 14, 8, Ink, TextAnchor.MiddleLeft);
            Text(card, Fit(KindLabel(effect), 6, CW - 104), 38, 20, CW - 104, 12, 6, Muted, TextAnchor.MiddleLeft);
            Label(card, T("change"), CW - 62, 10, 56, 18, () => StartPicker(false));

            const float x = 62f, w = CW - x;
            if (effect.Kind == EffectKind.Stat)
            {
                Row(block, T("amount"), 66);
                float step = 26f, input = w - 4 * step;
                Step(block, "-10", x, 66, -10); Step(block, "-1", x + step, 66, -1);
                Input(block, effect.Amount.ToString(CultureInfo.InvariantCulture), T("amount"), x + 2 * step, 66, input - 2, 18, value =>
                {
                    if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) && !float.IsNaN(amount) && !float.IsInfinity(amount))
                        effect.Amount = Mathf.Clamp(amount, -9999, 9999);
                });
                Step(block, "+1", x + 2 * step + input, 66, 1); Step(block, "+10", x + 3 * step + input, 66, 10);
            }
            else
            {
                Row(block, T("duration"), 66);
                Segment(block, x, 66, w, new[] { T("while_member"), T("permanent") }, effect.Lifetime == Lifetime.Permanent ? 1 : 0,
                    i => effect.Lifetime = i == 1 ? Lifetime.Permanent : Lifetime.Membership);
            }
            Row(block, T("status"), 88);
            Segment(block, x, 88, w, new[] { T("enabled"), T("disabled") }, effect.Enabled ? 0 : 1, i => effect.Enabled = i == 0);
            Footer(block, 120, () =>
            {
                if (string.IsNullOrEmpty(effect.Value)) return;
                RuleSet set = Current;
                int index = set.Declarations.FindIndex(d => d.Id == effect.Id);
                if (index < 0) set.Declarations.Add(effect); else set.Declarations[index] = effect;
                effect = null; Save(set);
            }, () => { effect = null; Build(); }, exists ? (Action)(() =>
            { RuleSet set = Current; set.Declarations.RemoveAll(d => d.Id == effect.Id); effect = null; Save(set); }) : null);

            string helpText = effect.Kind == EffectKind.Trait ? "trait_help" : effect.Kind == EffectKind.Item ? "item_help" : "stat_help";
            HelpBlock(T(helpText));
        }

        static void Step(Transform parent, string text, float x, float y, float delta) =>
            Label(parent, text, x, y, 24, 18, () => { effect.Amount = Mathf.Clamp(effect.Amount + delta, -9999, 9999); Build(); });

        static void BuildRestrictionEditor()
        {
            bool exists = Current.Restrictions.Any(r => r.Id == draft.Id);
            RectTransform head = Block(66, "requirement");
            EditorHeader(head, T(exists ? "requirement" : "add_requirement"), () => { draft = null; Build(); });
            const float x = 62f, w = CW - x;
            Row(head, T("match"), 24);
            Segment(head, x, 24, w, new[] { T("all_short"), T("any_short") }, draft.Any ? 1 : 0, i => draft.Any = i == 1);
            Row(head, T("status"), 46);
            Segment(head, x, 46, w, new[] { T("enabled"), T("disabled") }, draft.Enabled ? 0 : 1, i => draft.Enabled = i == 0);

            NativeUi.Group conditions = NativeUi.CreateGroup(content, T("conditions"), HeaderGold);
            if (conditions.Counter != null) conditions.Counter.text = draft.Conditions.Count.ToString();
            for (int i = 0; i < draft.Conditions.Count; i++)
            {
                int index = i; Condition condition = draft.Conditions[i];
                RectTransform token = Button(conditions.Grid, 0, 0, Cell, Cell, Slot, () =>
                { selectedCondition = selectedCondition == index ? -1 : index; Build(); });
                DrawCondition(token, condition, 3, 3, Cell - 6);
                MetaSymbols.Tip(token, () => Describe(condition));
                if (selectedCondition == index) Frame(token, 0);
            }
            Small(conditions.Grid, "+", 0, 0, Cell, () => StartPicker(true), T("add_condition"));

            if (selectedCondition >= 0 && selectedCondition < draft.Conditions.Count)
            {
                Condition condition = draft.Conditions[selectedCondition];
                RectTransform row = Block(40, "condition");
                Text(row, Fit(Describe(condition), 7, CW), 0, 0, CW, 16, 7, Ink, TextAnchor.MiddleCenter);
                Label(row, T("invert_condition"), 0, 20, CW / 2 - 2, 18, () => { condition.Negated = !condition.Negated; Build(); });
                Label(row, T("remove"), CW / 2 + 2, 20, CW / 2 - 2, 18, () => { draft.Conditions.RemoveAt(selectedCondition); selectedCondition = -1; Build(); }, Red);
            }
            else Hint(T(draft.Conditions.Count == 0 ? "first_condition" : "select_condition"));

            Footer(Block(20, "footer"), 0, () =>
            {
                if (draft.Conditions.Count == 0) return;
                RuleSet set = Current;
                int index = set.Restrictions.FindIndex(r => r.Id == draft.Id);
                if (index < 0) set.Restrictions.Add(draft); else set.Restrictions[index] = draft;
                draft = null; Save(set);
            }, () => { draft = null; Build(); }, exists ? (Action)(() =>
            { RuleSet set = Current; set.Restrictions.RemoveAll(r => r.Id == draft.Id); draft = null; Save(set); }) : null);
        }

        static void BuildHelp()
        {
            EditorHeader(Block(18, "help"), T("help"), () => { help = false; Build(); });
            var parts = new List<string> { T("admission_help"), T("trait_help"), T("item_help"), T("stat_help"), T("provenance_help") };
            if (selected.Kind == MetaKind.Subspecies) parts.Add(T("subspecies_help"));
            foreach (string part in parts) HelpBlock(part);
        }

        // A wrapped paragraph whose block grows to fit its text.
        static void HelpBlock(string text)
        {
            RectTransform block = Block(10, "paragraph");
            Text label = Paragraph(block, text, 2, 0, CW - 4, 10, 7, TextAnchor.UpperLeft);
            float height = Mathf.Ceil(label.preferredHeight) + 2;
            block.sizeDelta = new Vector2(CW, height);
            ((RectTransform)label.transform).sizeDelta = new Vector2(CW - 4, height);
        }

        static RectTransform Hint(string text)
        {
            RectTransform block = Block(24, "hint");
            Paragraph(block, text, 4, 0, CW - 8, 24, 6, TextAnchor.UpperCenter);
            return block;
        }

        // ── Picker: native groups, native order, one continuous scroll.
        static void StartPicker(bool forCondition)
        {
            picking = true; pickingCondition = forCondition; category = 0; query = "";
            Build();
        }

        static void LeavePicker()
        {
            picking = false;
            if (effect != null && string.IsNullOrEmpty(effect.Value)) effect = null;
            if (draft != null && draft.Conditions.Count == 0) draft = null;
            Build();
        }

        static void BuildPicker()
        {
            float y = ViewTop + 2f;
            string title = T(pickingCondition ? "add_condition" : effect != null && !string.IsNullOrEmpty(effect.Value) ? "change_selection" : "add_declaration");
            Small(root, "‹", Left, y, 16, LeavePicker, T("back"));
            Text(root, Fit(title, 8, CW - 22), Left + 22, y, CW - 22, 16, 8, HeaderGold, TextAnchor.MiddleLeft);
            y += 19f;
            if (pickingCondition)
            {
                // Trait plus one tab per meta type: icons only, named in tooltips.
                const int tabs = 10;
                float tab = (CW - (tabs - 1) * 2f) / tabs;
                for (int i = 0; i < tabs; i++)
                {
                    int filter = i;
                    Sprite icon = i == 0 ? MetaSymbols.TraitSprite("strong") : MetaSymbols.KindSprite((MetaKind)(i - 1));
                    string label = i == 0 ? T("traits") : KindName((MetaKind)(i - 1));
                    RectTransform token = Symbol(root, Left + i * (tab + 2f), y, tab, icon, null, () => SelectTab(filter), label);
                    if (category == i) Frame(token, 0);
                }
            }
            else
            {
                string[] names = { T("traits"), T("equipment"), T("stats") };
                Sprite[] icons = { MetaSymbols.TraitSprite("strong"), MetaSymbols.EquipmentSprite(), MetaSymbols.StatSprite("damage") };
                float tab = (CW - 2 * 2f) / 3f;
                for (int i = 0; i < 3; i++)
                {
                    int filter = i;
                    RectTransform token = Button(root, Left + i * (tab + 2f), y, tab, 20, Slot, () => SelectTab(filter));
                    Picture(token, icons[i], 4, 3, 14, 14);
                    Text(token, Fit(names[i], 7, tab - 22), 20, 0, tab - 22, 20, 7, category == i ? Gold : Muted, TextAnchor.MiddleLeft);
                    if (category == i) Frame(token, 0);
                }
            }
            y += 23f;
            Input(root, query, T("search_live"), Left, y, CW, 17, value => { query = value; Relayout(); });
            BuildResults();
        }

        static void SelectTab(int value)
        {
            if (category == value) return;
            category = value; query = "";
            Build();
        }

        static void BuildResults()
        {
            results = Choices();
            foreach (Group group in results)
            {
                group.View = NativeUi.CreateGroup(content, group.Title, group.Color);
                if (group.Banners && group.View.Grid.GetComponent<GridLayoutGroup>() is GridLayoutGroup grid)
                {
                    grid.constraint = GridLayoutGroup.Constraint.Flexible;
                    grid.cellSize = new Vector2(34f, 40f);
                }
                foreach (Choice entry in group.Items) entry.View = entry.Create(group.View.Grid);
            }
            Hint(T("no_results")).name = "no_results";
            Relayout();
        }

        static void Relayout()
        {
            if (results == null) return;
            int total = 0;
            foreach (Group group in results)
            {
                int shown = 0;
                foreach (Choice entry in group.Items)
                {
                    bool visible = Matches(entry.Search, query);
                    entry.View.gameObject.SetActive(visible);
                    if (visible) shown++;
                }
                group.View.Root.gameObject.SetActive(shown > 0);
                if (group.View.Counter != null) group.View.Counter.text = shown.ToString();
                total += shown;
            }
            content.Find("no_results")?.gameObject.SetActive(total == 0);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = Vector2.zero;
        }

        static List<Group> Choices()
        {
            if (pickingCondition && category > 0) return MetaChoices((MetaKind)(category - 1));
            if (category == 0) return TraitChoices();
            if (category == 1) return ItemChoices();
            return StatChoices();
        }

        // Same order as the native trait editor: by group, then priority, then id.
        static List<Group> TraitChoices()
        {
            var used = new HashSet<string>(pickingCondition
                ? draft.Conditions.Where(c => c.Kind == ConditionKind.Trait).Select(c => c.Trait)
                : Current.Declarations.Where(d => d.Kind == EffectKind.Trait).Select(d => d.Value));
            var groups = new List<Group>();
            var byId = new Dictionary<string, Group>();
            foreach (ActorTraitGroupAsset asset in AssetManager.trait_groups.list)
            {
                var group = new Group { Title = Locale(asset.getLocaleID(), asset.id), Color = GroupColor(asset) };
                groups.Add(group);
                byId[asset.id] = group;
            }
            var other = new Group { Title = T("other") };
            var traits = new List<ActorTrait>(AssetManager.traits.list);
            traits.Sort((a, b) =>
            {
                int order = b.priority.CompareTo(a.priority);
                return order != 0 ? order : StringComparer.Ordinal.Compare(a.id, b.id);
            });
            foreach (ActorTrait trait in traits)
            {
                ActorTrait entry = trait;
                Group group = entry.group_id != null && byId.TryGetValue(entry.group_id, out Group known) ? known : other;
                string name = TraitName(entry.id);
                bool marked = used.Contains(entry.id);
                Action select = () =>
                {
                    if (pickingCondition) { draft.Conditions.Add(new Condition { Trait = entry.id }); selectedCondition = -1; }
                    else SetEffect(EffectKind.Trait, entry.id);
                    picking = false; Build();
                };
                group.Items.Add(new Choice
                {
                    Search = name + " " + entry.id,
                    Create = grid => NativeUi.TraitButton(grid, entry, marked, select)
                        ?? Marked(Symbol(grid, 0, 0, Cell, entry.getSprite(), null, select, name, group.Title), marked)
                });
            }
            groups.Add(other);
            return groups;
        }

        // Same order as the native equipment editor: by item group, then library order.
        static List<Group> ItemChoices()
        {
            var used = new HashSet<string>(Current.Declarations.Where(d => d.Kind == EffectKind.Item).Select(d => d.Value));
            var groups = new List<Group>();
            var byId = new Dictionary<string, Group>();
            foreach (ItemGroupAsset asset in AssetManager.item_groups.list)
            {
                var group = new Group { Title = Locale(asset.getLocaleID(), asset.id), Color = GroupColor(asset) };
                groups.Add(group);
                byId[asset.id] = group;
            }
            var other = new Group { Title = T("other") };
            foreach (EquipmentAsset item in AssetManager.items.list)
            {
                if (!item.show_in_meta_editor || !item.can_be_given) continue;
                EquipmentAsset entry = item;
                Group group = entry.group_id != null && byId.TryGetValue(entry.group_id, out Group known) ? known : other;
                string name = ItemName(entry.id);
                bool marked = used.Contains(entry.id);
                Action select = () => { SetEffect(EffectKind.Item, entry.id); picking = false; Build(); };
                group.Items.Add(new Choice
                {
                    Search = name + " " + entry.id,
                    Create = grid => NativeUi.ItemButton(grid, entry, marked, select)
                        ?? Marked(Symbol(grid, 0, 0, Cell, entry.getSprite(), null, select, name, SlotName(entry.id)), marked)
                });
            }
            groups.Add(other);
            return groups;
        }

        static List<Group> StatChoices()
        {
            var used = new HashSet<string>(Current.Declarations.Where(d => d.Kind == EffectKind.Stat).Select(d => d.Value));
            var group = new Group { Title = T("stats") };
            foreach (string value in WorldRules.Stats)
            {
                string stat = value;
                string name = T("stat_" + stat);
                Action select = () => { SetEffect(EffectKind.Stat, stat); if (effect.Amount == 0) effect.Amount = 10; picking = false; Build(); };
                group.Items.Add(new Choice
                {
                    Search = name + " " + stat,
                    Create = grid => Marked(Symbol(grid, 0, 0, Cell, MetaSymbols.StatSprite(stat), null, select, name, T("stat")), used.Contains(stat))
                });
            }
            return new List<Group> { group };
        }

        static List<Group> MetaChoices(MetaKind type)
        {
            var group = new Group { Title = KindName(type), Banners = true };
            foreach (MetaHandle value in WorldRules.Metas)
            {
                MetaHandle meta = value;
                if (meta.Kind != type) continue;
                bool marked = draft.Conditions.Any(c => c.Kind == ConditionKind.Membership && c.Meta == meta.Kind && c.MetaId == meta.Id);
                Action select = () =>
                {
                    draft.Conditions.Add(new Condition { Kind = ConditionKind.Membership, Meta = meta.Kind, MetaId = meta.Id });
                    selectedCondition = -1; picking = false; Build();
                };
                group.Items.Add(new Choice { Search = meta.Name, Create = grid => Marked(NativeUi.Banner(grid, meta, 34, select), marked) });
            }
            return new List<Group> { group };
        }

        static RectTransform Marked(RectTransform token, bool marked)
        {
            if (marked) Frame(token, 0);
            return token;
        }

        static Color GroupColor(BaseCategoryAsset asset)
        {
            try { return string.IsNullOrEmpty(asset.color) ? HeaderGold : asset.getColor(); }
            catch (Exception) { return HeaderGold; }
        }

        static void SetEffect(EffectKind kind, string value)
        {
            if (effect.Kind != kind) effect.Amount = 0;
            effect.Kind = kind; effect.Value = value;
        }

        static void Save(RuleSet set)
        {
            WorldRules.Save(selected, set);
            selectedCondition = -1;
            Build();
        }

        static bool Matches(string value, string needle) => string.IsNullOrEmpty(needle) || value.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;

        // ── Controls
        static RectTransform Symbol(Transform parent, float x, float y, float size, Sprite sprite, MetaHandle meta, Action click, string name, string description = "")
        {
            RectTransform token = Button(parent, x, y, size, size, Slot, click);
            if (meta != null) MetaSymbols.Draw(token, meta, 2, 2, size - 4);
            else if (sprite != null) Picture(token, sprite, 4, 4, size - 8, size - 8);
            MetaSymbols.Tip(token, () => name, () => description);
            return token;
        }

        static void Badge(RectTransform token, string value)
        {
            Rect(token, 1, Cell - 10, Cell - 2, 9, new Color(0.08f, 0.08f, 0.07f, 0.85f));
            Text(token, value, 0, Cell - 10, Cell, 9, 6, Gold, TextAnchor.MiddleCenter);
        }

        static void Corner(RectTransform token, Color color)
        {
            Rect(token, Cell - 8, 2, 6, 6, Edge);
            Rect(token, Cell - 7, 3, 4, 4, color);
        }

        static RectTransform Small(Transform parent, string text, float x, float y, float size, Action click, string tooltip)
        {
            RectTransform button = Label(parent, text, x, y, size, size, click);
            MetaSymbols.Tip(button, () => tooltip);
            return button;
        }

        static RectTransform Label(Transform parent, string text, float x, float y, float w, float h, Action click, string sprite = Gray)
        {
            if (SpriteTextureLoader.getSprite(sprite) == null) sprite = Gray;
            RectTransform button = Button(parent, x, y, w, h, sprite, click);
            int size = h >= 24 ? 9 : 7;
            Text(button, Fit(text, size, w - 4), 2, 0, w - 4, h, size, Ink, TextAnchor.MiddleCenter);
            return button;
        }

        // Mutually exclusive choices; the active one is framed like native selections.
        static void Segment(Transform parent, float x, float y, float w, string[] options, int active, Action<int> pick)
        {
            float gap = 2f, each = (w - gap * (options.Length - 1)) / options.Length;
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                RectTransform button = Button(parent, x + i * (each + gap), y, each, 18, Slot, () => { if (index != active) { pick(index); Build(); } });
                Text(button, Fit(options[i], 7, each - 4), 2, 0, each - 4, 18, 7, i == active ? Gold : Muted, TextAnchor.MiddleCenter);
                if (i == active) Frame(button, 0);
            }
        }

        static void Footer(Transform parent, float y, Action save, Action cancel, Action remove)
        {
            if (remove != null) Label(parent, T("remove"), 0, y, 60, 20, remove, Red);
            Label(parent, T("cancel"), CW - 126, y, 60, 20, cancel);
            Label(parent, T("save"), CW - 62, y, 62, 20, save);
        }

        static Text Paragraph(Transform parent, string value, float x, float y, float w, float h, int size = 7, TextAnchor align = TextAnchor.UpperLeft)
        {
            Text text = Text(parent, value, x, y, w, h, size, Muted, align).GetComponent<Text>();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }

        static void Input(Transform parent, string value, string placeholder, float x, float y, float w, float h, Action<string> change)
        {
            string sprite = SpriteTextureLoader.getSprite(Field) != null ? Field : Slot;
            RectTransform frame = Sliced(parent, sprite, x, y, w, h);
            Image image = frame.GetComponent<Image>(); image.raycastTarget = true;
            InputField input = frame.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            Text text = Text(frame, "", 5, 0, w - 10, h, 7, Ink, TextAnchor.MiddleLeft).GetComponent<Text>();
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent = text;
            input.placeholder = Text(frame, placeholder, 5, 0, w - 10, h, 7, Muted, TextAnchor.MiddleLeft).GetComponent<Text>();
            input.text = value; input.characterLimit = 120;
            input.onValueChanged.AddListener(valueChanged => change(valueChanged));
        }
    }
}
