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
    // One individual meta, two sections. Controls for a rule live in its editor.
    public static class RulesWindow
    {
        const string WindowId = "mr_window";
        const float W = 390f, H = 360f, C = W - 24f;
        const int Columns = 8, PageSize = 48;
        static ScrollWindow window;
        static RectTransform root, pickerResults;
        static MetaHandle selected;
        static MapStats world;
        static Restriction draft;
        static Declaration effect;
        static int selectedCondition = -1;
        static bool picking, pickingCondition, advanced, settings;
        static int category, page;
        static string query = "";
        static Text summaryLabel;
        static int memberCount, incompatibleCount;
        static float nextSummary;

        sealed class Choice
        {
            public string Name, Detail;
            public Sprite Sprite;
            public MetaHandle Meta;
            public Action Select;
        }

        public static string T(string key) => LM.Get("mr_" + key);
        public static string KindName(MetaKind value) => T("kind_" + value.ToString().ToLowerInvariant());
        static string TraitName(string id) => LocalizedTextManager.stringExists("trait_" + id) ? LM.Get("trait_" + id) : id;
        static RuleSet Current => WorldRules.Get(selected) ?? new RuleSet();

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
            FitWindow();
        }

        static void ResetEditor()
        {
            draft = null; effect = null; selectedCondition = -1;
            picking = false; advanced = false; settings = false;
            query = ""; page = 0;
        }

        static void Create()
        {
            window = WindowCreator.CreateEmptyWindow(WindowId, "mr_title");
            ScrollWindow.finishAnimations();
            Transform bg = window.transform.Find("Background");
            bg.Find("Scroll View")?.gameObject.SetActive(false);
            bg.GetComponent<RectTransform>().sizeDelta = new Vector2(W, H);
            bg.GetComponent<Image>().type = Image.Type.Sliced;
            Transform close = window.transform.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(W / 2f - 18f, H / 2f + 4f);
            window.titleText.transform.localPosition = new Vector3(0, H / 2f - 2f);
            root = Box(bg, 0, 0, W, H, "mr_root");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
        }

        static void FitWindow()
        {
            RectTransform canvas = window.GetComponentInParent<Canvas>()?.transform as RectTransform;
            if (canvas == null) return;
            float scale = Mathf.Min(1f, (canvas.rect.width - 80f) / (W + 16f), (canvas.rect.height - 35f) / (H + 20f));
            window.transform.localScale = new Vector3(Mathf.Max(0.35f, scale), Mathf.Max(0.35f, scale), 1);
        }

        static void Clear(Transform parent)
        {
            foreach (Transform child in parent)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
        }

        static void Build()
        {
            if (root == null) return;
            Tooltip.hideTooltip();
            Clear(root);
            pickerResults = null;
            summaryLabel = null;
            if (!ReferenceEquals(world, World.world?.map_stats) || selected == null ||
                !WorldRules.Metas.Any(m => m.Target == selected.Target && m.Id == selected.Id))
            {
                Paragraph(root, T("choose_meta"), 24, 110, W - 48, 70);
                return;
            }
            BuildIdentity();
            if (WorldRules.IsBroken(selected)) { Paragraph(root, T("invalid_data"), 20, 100, W - 40, 100); return; }
            RectTransform pane = Box(root, 12, 84, C, 260, "rules_content");
            if (picking) BuildPicker(pane);
            else if (settings) BuildSettings(pane);
            else if (draft != null) BuildRestrictionEditor(pane);
            else if (effect != null) BuildDeclarationEditor(pane);
            else BuildOverview(pane);
        }

        static void BuildIdentity()
        {
            RectTransform emblem = Button(root, 16, 24, 44, 44, null, ReturnToMeta);
            MetaSymbols.Draw(emblem, selected, 0, 0, 44);
            MetaSymbols.Tip(emblem, () => selected.Name, () => T("return_meta"));
            Text(root, Fit(selected.Name, 12, C - 90), 72, 25, C - 90, 22, 12, Ink, TextAnchor.MiddleLeft);
            Text(root, KindName(selected.Kind) + (Current.Enabled ? "" : " · " + T("paused")), 72, 48, C - 95, 14, 7, Muted, TextAnchor.MiddleLeft);
            Small(root, "…", W - 38, 47, 21, () => { ResetEditor(); settings = true; Build(); }, T("options"));
        }

        static void ReturnToMeta()
        {
            NativeMetaWindows.OpenNative(selected);
        }

        static RectTransform Section(RectTransform pane, string title, float y, Action add)
        {
            RectTransform card = Sliced(pane, Slot, 0, y, C, 112);
            Text(card, title, 12, 4, C - 52, 20, 10, HeaderGold, TextAnchor.MiddleLeft);
            Rect(card, 10, 26, C - 20, 1, Line);
            Small(card, "+", C - 33, 4, 23, add, title);
            return card;
        }

        static void BuildOverview(RectTransform pane)
        {
            RuleSet set = Current;
            RectTransform declarations = Section(pane, T("declarations"), 0, () =>
            { effect = new Declaration(); StartPicker(false); });
            RectTransform requirements = Section(pane, T("requirements"), 124, () =>
            { draft = new Restriction(); StartPicker(true); });

            if (set.Declarations.Count == 0) Empty(declarations, T("no_declarations"));
            else
            {
                float rows = (set.Declarations.Count + Columns - 1) / Columns;
                RectTransform grid = Scroll(declarations, "declarations", 11, 32, C - 22, 72, rows * 38);
                for (int i = 0; i < set.Declarations.Count; i++)
                {
                    Declaration item = set.Declarations[i];
                    RectTransform token = Symbol(grid, i % Columns * 43, i / Columns * 38, 32,
                        item.Kind == EffectKind.Trait ? MetaSymbols.TraitSprite(item.Value) : MetaSymbols.StatSprite(item.Value), null,
                        () => { effect = Clone(item); Build(); }, DeclarationName(item),
                        T(item.Kind == EffectKind.Stat || item.Lifetime == Lifetime.Membership ? "while_member" : "permanent"));
                    if (!item.Enabled || !set.Enabled) Fade(token);
                    if (item.Kind == EffectKind.Stat) Badge(token, Number(item.Amount));
                    else if (item.Lifetime == Lifetime.Permanent) Rect(token, 25, 3, 4, 4, Gold);
                }
            }
            if (set.Restrictions.Count == 0) Empty(requirements, T("no_requirements"));
            else
            {
                RectTransform list = Scroll(requirements, "requirements", 11, 32, C - 22, 72, set.Restrictions.Count * 37);
                for (int i = 0; i < set.Restrictions.Count; i++)
                {
                    Restriction item = set.Restrictions[i];
                    RectTransform row = Button(list, 0, i * 37, C - 22, 33, Slot,
                        () => { draft = Clone(item); selectedCondition = -1; Build(); });
                    int shown = Math.Min(item.Conditions.Count, 7);
                    for (int j = 0; j < shown; j++) DrawCondition(row, item.Conditions[j], 5 + j * 43, 3, 27);
                    if (item.Conditions.Count > shown) Text(row, "+" + (item.Conditions.Count - shown), C - 52, 5, 25, 22, 8, Muted, TextAnchor.MiddleCenter);
                    MetaSymbols.Tip(row, () => T(item.Any ? "any" : "all"), () => string.Join("\n", item.Conditions.Select(Describe)));
                    if (!item.Enabled || !set.Enabled) Fade(row);
                }
            }
            RectTransform summary = Button(pane, 2, 243, C - 4, 15, null, () => { });
            summaryLabel = Text(summary, "", 0, 0, C - 4, 15, 7, Muted, TextAnchor.MiddleCenter).GetComponent<Text>();
            UpdateSummary();
            MetaSymbols.Tip(summary, () => T("members") + ": " + memberCount,
                () => T("violations") + ": " + incompatibleCount + "\n" + T("admission_help") +
                    (selected.Kind == MetaKind.Subspecies ? "\n" + T("subspecies_help") : ""));
        }

        static void UpdateSummary()
        {
            memberCount = WorldRules.MemberCount(selected, out incompatibleCount);
            summaryLabel.text = memberCount + " " + T("members").ToLowerInvariant() +
                (incompatibleCount > 0 ? " · " + incompatibleCount + " " + T("incompatible") : "");
        }

        static void Empty(RectTransform card, string message) => Text(card, message, 12, 45, C - 24, 30, 8, Muted, TextAnchor.MiddleCenter);
        static void Fade(RectTransform target) => target.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
        static string Number(float n) => n.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
        static string DeclarationName(Declaration d) => d.Kind == EffectKind.Trait ? TraitName(d.Value) : T("stat_" + d.Value) + " " + Number(d.Amount);
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
            if (c.Kind == ConditionKind.Trait) Picture(parent, MetaSymbols.TraitSprite(c.Trait), x + 2, y + 2, size - 4, size - 4);
            else if (Referenced(c) is MetaHandle meta) MetaSymbols.Draw(parent, meta, x + 2, y + 2, size - 4);
            else Picture(parent, MetaSymbols.KindSprite(c.Meta), x + 2, y + 2, size - 4, size - 4);
            if (c.Negated)
            {
                RectTransform slash = Rect(parent, x + 2, y + size / 2, size - 4, 2, Hex("#dc6a58"));
                slash.pivot = new Vector2(0.5f, 0.5f);
                slash.anchoredPosition = new Vector2(x + size / 2, -(y + size / 2));
                slash.localRotation = Quaternion.Euler(0, 0, 45);
            }
        }

        static void EditorHeader(RectTransform pane, string title, Action back)
        {
            Small(pane, "‹", 0, 0, 22, back, T("back"));
            Text(pane, title, 30, 0, C - 62, 23, 9, Gold, TextAnchor.MiddleCenter);
        }

        static void BuildDeclarationEditor(RectTransform pane)
        {
            EditorHeader(pane, T("declaration"), () => { effect = null; advanced = false; Build(); });
            Small(pane, "…", C - 22, 0, 22, () => { advanced = !advanced; Build(); }, T("options"));
            Symbol(pane, C / 2 - 24, 35, 48, effect.Kind == EffectKind.Trait ? MetaSymbols.TraitSprite(effect.Value) : MetaSymbols.StatSprite(effect.Value), null,
                () => StartPicker(false), DeclarationName(effect), T("change_selection"));
            Text(pane, Fit(effect.Kind == EffectKind.Trait ? TraitName(effect.Value) : T("stat_" + effect.Value), 10, C - 20), 10, 92, C - 20, 22, 10, Ink, TextAnchor.MiddleCenter);
            if (effect.Kind == EffectKind.Trait)
            {
                Label(pane, T(effect.Lifetime == Lifetime.Membership ? "while_member" : "permanent"), 40, 130, C - 80, 24,
                    () => { effect.Lifetime = effect.Lifetime == Lifetime.Membership ? Lifetime.Permanent : Lifetime.Membership; Build(); });
            }
            else
            {
                Input(pane, effect.Amount.ToString(CultureInfo.InvariantCulture), T("amount"), 85, 130, C - 170, value =>
                {
                    if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) && !float.IsNaN(amount) && !float.IsInfinity(amount))
                        effect.Amount = Mathf.Clamp(amount, -9999, 9999);
                });
            }
            if (advanced) Label(pane, T(effect.Enabled ? "rule_on" : "rule_off"), 40, 166, C - 80, 21, () => { effect.Enabled = !effect.Enabled; Build(); });
            Footer(pane, () =>
            {
                if (string.IsNullOrEmpty(effect.Value)) return;
                RuleSet set = Current;
                int index = set.Declarations.FindIndex(d => d.Id == effect.Id);
                if (index < 0) set.Declarations.Add(effect); else set.Declarations[index] = effect;
                effect = null; Save(set);
            }, Current.Declarations.Any(d => d.Id == effect.Id) ? (Action)(() =>
            { RuleSet set = Current; set.Declarations.RemoveAll(d => d.Id == effect.Id); effect = null; Save(set); }) : null);
        }

        static void BuildRestrictionEditor(RectTransform pane)
        {
            EditorHeader(pane, T("requirement"), () => { draft = null; advanced = false; Build(); });
            Small(pane, "…", C - 22, 0, 22, () => { selectedCondition = -1; advanced = !advanced; Build(); }, T("options"));
            RectTransform card = Sliced(pane, Slot, 0, 34, C, 100);
            int cells = draft.Conditions.Count + 1;
            RectTransform grid = Scroll(card, "conditions", 10, 10, C - 20, 80, ((cells + Columns - 1) / Columns) * 38);
            for (int i = 0; i < draft.Conditions.Count; i++)
            {
                int index = i; Condition condition = draft.Conditions[i];
                RectTransform token = Button(grid, i % Columns * 43, i / Columns * 38, 32, 32, Slot, () => { selectedCondition = index; Build(); });
                DrawCondition(token, condition, 0, 0, 32);
                MetaSymbols.Tip(token, () => Describe(condition));
                if (selectedCondition == index) Frame(token, 0);
            }
            int count = draft.Conditions.Count;
            Small(grid, "+", count % Columns * 43, count / Columns * 38, 32, () => StartPicker(true), T("add_condition"));
            if (selectedCondition >= 0 && selectedCondition < draft.Conditions.Count)
            {
                Condition condition = draft.Conditions[selectedCondition];
                Text(pane, Fit(Describe(condition), 8, C - 20), 10, 145, C - 20, 20, 8, Ink, TextAnchor.MiddleCenter);
                Label(pane, T("invert_condition"), 20, 171, 155, 21, () => { condition.Negated = !condition.Negated; Build(); });
                Label(pane, T("remove"), 191, 171, 155, 21, () => { draft.Conditions.RemoveAt(selectedCondition); selectedCondition = -1; Build(); });
            }
            else if (advanced)
            {
                Label(pane, T(draft.Any ? "any" : "all"), 20, 150, C - 40, 22, () => { draft.Any = !draft.Any; Build(); });
                Label(pane, T(draft.Enabled ? "rule_on" : "rule_off"), 20, 180, C - 40, 21, () => { draft.Enabled = !draft.Enabled; Build(); });
            }
            Footer(pane, () =>
            {
                if (draft.Conditions.Count == 0) return;
                RuleSet set = Current;
                int index = set.Restrictions.FindIndex(r => r.Id == draft.Id);
                if (index < 0) set.Restrictions.Add(draft); else set.Restrictions[index] = draft;
                draft = null; Save(set);
            }, Current.Restrictions.Any(r => r.Id == draft.Id) ? (Action)(() =>
            { RuleSet set = Current; set.Restrictions.RemoveAll(r => r.Id == draft.Id); draft = null; Save(set); }) : null);
        }

        static void BuildSettings(RectTransform pane)
        {
            EditorHeader(pane, T("options"), () => { settings = false; Build(); });
            RuleSet set = Current;
            Label(pane, T(set.Enabled ? "rules_on" : "rules_off"), 35, 50, C - 70, 26, () => { set.Enabled = !set.Enabled; Save(set); });
            Paragraph(pane, T("provenance_help"), 20, 103, C - 40, 65);
            Paragraph(pane, T("admission_help"), 20, 174, C - 40, 65);
        }

        static void StartPicker(bool forCondition)
        {
            picking = true; pickingCondition = forCondition; category = 0; query = ""; page = 0;
            Build();
        }

        static void LeavePicker()
        {
            picking = false;
            if (effect != null && string.IsNullOrEmpty(effect.Value)) effect = null;
            if (draft != null && draft.Conditions.Count == 0) draft = null;
            Build();
        }

        static void BuildPicker(RectTransform pane)
        {
            EditorHeader(pane, T(pickingCondition ? "add_requirement" : "add_declaration"), LeavePicker);
            int filters = pickingCondition ? 10 : 2;
            for (int i = 0; i < filters; i++)
            {
                int filter = i;
                Sprite icon = i == 0 ? MetaSymbols.TraitSprite("strong") :
                    pickingCondition ? MetaSymbols.KindSprite((MetaKind)(i - 1)) : MetaSymbols.StatSprite("damage");
                string label = i == 0 ? T("trait") : pickingCondition ? KindName((MetaKind)(i - 1)) : T("stat");
                RectTransform token = Symbol(pane, i * 35, 31, 29, icon, null,
                    () => { category = filter; query = ""; page = 0; Build(); }, label);
                if (category == i) Frame(token, 0);
            }
            Input(pane, query, T("search_live"), 0, 68, C, value => { query = value; page = 0; BuildPickerResults(); });
            pickerResults = Box(pane, 0, 96, C, 164, "picker_results");
            BuildPickerResults();
        }

        static void BuildPickerResults()
        {
            if (pickerResults == null) return;
            Clear(pickerResults);
            var choices = new List<Choice>();
            if (category == 0)
            {
                foreach (ActorTrait trait in AssetManager.traits.list)
                {
                    ActorTrait entry = trait;
                    string name = TraitName(entry.id);
                    if (!Matches(name + " " + entry.id, query)) continue;
                    choices.Add(new Choice { Name = name, Detail = entry.id, Sprite = entry.getSprite(), Select = () =>
                    {
                        if (pickingCondition) { draft.Conditions.Add(new Condition { Trait = entry.id }); selectedCondition = -1; }
                        else { effect.Kind = EffectKind.Trait; effect.Value = entry.id; }
                        picking = false; Build();
                    }});
                }
            }
            else if (pickingCondition)
            {
                MetaKind type = (MetaKind)(category - 1);
                foreach (MetaHandle value in WorldRules.Metas)
                {
                    MetaHandle meta = value;
                    if (meta.Kind != type || !Matches(meta.Name, query)) continue;
                    choices.Add(new Choice { Name = meta.Name, Detail = KindName(meta.Kind), Meta = meta, Select = () =>
                    {
                        draft.Conditions.Add(new Condition { Kind = ConditionKind.Membership, Meta = meta.Kind, MetaId = meta.Id });
                        selectedCondition = -1; picking = false; Build();
                    }});
                }
            }
            else
            {
                foreach (string value in WorldRules.AllowedStats)
                {
                    string stat = value;
                    if (!Matches(T("stat_" + stat), query)) continue;
                    choices.Add(new Choice { Name = T("stat_" + stat), Detail = T("stat"), Sprite = MetaSymbols.StatSprite(stat), Select = () =>
                    { effect.Kind = EffectKind.Stat; effect.Value = stat; if (effect.Amount == 0) effect.Amount = 10; picking = false; Build(); }});
                }
            }
            choices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            int pages = Math.Max(1, (choices.Count + PageSize - 1) / PageSize);
            page = Math.Min(page, pages - 1);
            var visible = choices.Skip(page * PageSize).Take(PageSize).ToList();
            RectTransform grid = Scroll(pickerResults, "symbols", 0, 0, C, 136, ((visible.Count + Columns - 1) / Columns) * 43);
            for (int i = 0; i < visible.Count; i++)
            {
                Choice entry = visible[i];
                Symbol(grid, 6 + i % Columns * 45, i / Columns * 43, 37, entry.Sprite, entry.Meta, entry.Select, entry.Name, entry.Detail);
            }
            if (visible.Count == 0) Text(pickerResults, T("no_results"), 0, 45, C, 30, 8, Muted, TextAnchor.MiddleCenter);
            if (pages > 1)
            {
                Small(pickerResults, "‹", C / 2 - 65, 140, 22, () => { page = Math.Max(0, page - 1); BuildPickerResults(); }, T("back"));
                Text(pickerResults, (page + 1) + " / " + pages, C / 2 - 35, 140, 70, 22, 7, Muted, TextAnchor.MiddleCenter);
                Small(pickerResults, "›", C / 2 + 43, 140, 22, () => { page = Math.Min(pages - 1, page + 1); BuildPickerResults(); }, T("next"));
            }
        }

        static void Save(RuleSet set)
        {
            WorldRules.Save(selected, set);
            selectedCondition = -1; advanced = false;
            Build();
        }

        static bool Matches(string value, string needle) => string.IsNullOrEmpty(needle) || value.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;

        static RectTransform Symbol(Transform parent, float x, float y, float size, Sprite sprite, MetaHandle meta, Action click, string name, string description = "")
        {
            RectTransform token = Button(parent, x, y, size, size, Slot, click);
            if (meta != null) MetaSymbols.Draw(token, meta, 4, 4, size - 8);
            else if (sprite != null) Picture(token, sprite, 5, 5, size - 10, size - 10);
            MetaSymbols.Tip(token, () => name, () => description);
            return token;
        }

        static void Badge(RectTransform token, string value)
        {
            Rect(token, 0, 22, 32, 11, Hex("#20231f"));
            Text(token, value, -3, 22, 38, 11, 7, Gold, TextAnchor.MiddleCenter);
        }

        static RectTransform Small(Transform parent, string text, float x, float y, float size, Action click, string tooltip)
        {
            RectTransform button = Label(parent, text, x, y, size, size, click);
            MetaSymbols.Tip(button, () => tooltip);
            return button;
        }

        static RectTransform Label(Transform parent, string text, float x, float y, float w, float h, Action click)
        {
            RectTransform button = Button(parent, x, y, w, h, "ui/special/special_buttonGray", click);
            Text(button, text, 3, 0, w - 6, h, h > 24 ? 10 : 8, Ink, TextAnchor.MiddleCenter);
            return button;
        }

        static void Footer(RectTransform pane, Action save, Action remove)
        {
            if (remove != null) Label(pane, T("remove"), 0, 230, 100, 24, remove);
            Label(pane, T("save"), C - 125, 230, 125, 24, save);
        }

        static void Paragraph(Transform parent, string value, float x, float y, float w, float h)
        {
            Text text = Text(parent, value, x, y, w, h, 8, Muted, TextAnchor.UpperLeft).GetComponent<Text>();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        static void Input(Transform parent, string value, string placeholder, float x, float y, float w, Action<string> change)
        {
            RectTransform frame = Sliced(parent, Slot, x, y, w, 22);
            Image image = frame.GetComponent<Image>(); image.raycastTarget = true;
            InputField input = frame.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            Text text = Text(frame, "", 6, 1, w - 12, 20, 8, Ink, TextAnchor.MiddleLeft).GetComponent<Text>();
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent = text;
            input.placeholder = Text(frame, placeholder, 6, 1, w - 12, 20, 8, Muted, TextAnchor.MiddleLeft).GetComponent<Text>();
            input.text = value; input.characterLimit = 120;
            input.onValueChanged.AddListener(valueChanged => change(valueChanged));
        }
    }
}
