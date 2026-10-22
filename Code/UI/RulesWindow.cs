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
        const float CW = 210f, Left = 15f, ViewW = 232f, ViewH = 286.2f, ViewY = -6.5f, ViewTop = 23.4f, PickerInset = 64f, ConditionPickerInset = 112f;
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
        static bool picking, pickingCondition, help, members, addingMembers;
        static string memberQuery = "";
        static string memberFeedback = "";
        static NativeUi.Group memberList;
        static bool onlyEligible;
        static bool IsEquipment => selected?.Kind == MetaKind.Equipment;
        static string People => T(IsEquipment ? "wearers" : "members");
        static string AddPeople => IsEquipment ? "add_wearer" : "add_members";
        static bool confirmEmpty;
        static int category;
        static string query = "";
        static Text summaryLabel;
        static int memberCount, incompatibleCount;
        static float nextSummary;
        static List<Group> results;
        static MetaHandle createdMeta;
        static string creationFeedback = "";

        public static bool HasCreationReturn => createdMeta != null && selected != null &&
            !SmoothLoader.isLoading() && ReferenceEquals(world, World.world?.map_stats) &&
            WorldRules.Metas.Any(m => ReferenceEquals(m.Target, selected.Target) && m.Id == selected.Id);

        public static void ReturnFromCreation()
        {
            if (!HasCreationReturn) { createdMeta = null; return; }
            WorldRules.RefreshMetas();
            createdMeta = null;
            ScrollWindow.showWindow(WindowId);
            Build();
        }

        public static void ResumeAfterNativeEditor()
        {
            if (!HasCreationReturn) return;
            WorldRules.RefreshMetas();
            createdMeta = null;
            Build();
        }

        sealed class Choice
        {
            public string Search;
            public bool AlwaysVisible;
            public Func<Transform, RectTransform> Create;
            public RectTransform View;
        }

        sealed class Group
        {
            public string Title;
            public Color Color = HeaderGold;
            public bool Banners;
            public bool Rows;
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
            if (HasCreationReturn && ReferenceEquals(meta.Target, createdMeta.Target)) { ReturnFromCreation(); return; }
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
            createdMeta = null; creationFeedback = "";
            draft = null; effect = null; selectedCondition = -1;
            picking = false; help = false; members = false; query = ""; memberQuery = "";
            addingMembers = false; onlyEligible = false; memberFeedback = "";
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
            window.gameObject.AddComponent<RulesWindowResume>();
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

        static void Build() => Build(false);

        static void Build(bool keepConfirm)
        {
            if (root == null) return;
            if (!keepConfirm) confirmEmpty = false;
            Tooltip.hideTooltip();
            Clear(root);
            Clear(content);
            results = null;
            summaryLabel = null;
            SetInset(picking ? pickingCondition ? ConditionPickerInset : PickerInset : 0f);
            try
            {
                if (!ReferenceEquals(world, World.world?.map_stats) || selected == null ||
                    !WorldRules.Metas.Any(m => m.Target == selected.Target && m.Id == selected.Id))
                    Paragraph(Block(80), T("choose_meta"), 0, 10, CW, 70);
                else if (picking) BuildPicker();
                else
                {
                    BuildIdentity();
                    if (WorldRules.IsBroken(selected)) Paragraph(Block(90), T("invalid_data"), 0, 0, CW, 90);
                    else if (help) BuildHelp();
                    else if (members) BuildMembers();
                    else if (draft != null) BuildRestrictionEditor();
                    else if (effect != null) BuildDeclarationEditor();
                    else BuildOverview();
                }
            }
            // Keep whatever was built usable; the error is in Player.log.
            catch (Exception error) { WorldRules.Report(error); }
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
            MetaSymbols.Tip(hover, () => People + ": " + memberCount,
                () => T("violations") + ": " + incompatibleCount + "\n\n" + T(IsEquipment ? "equipment_rules_help" : "admission_help") +
                    (selected.Kind == MetaKind.Subspecies ? "\n\n" + T("subspecies_help") : ""));
            UpdateSummary();

            RectTransform toggle = Button(block, 46, 30, 90, 17, Gray, () => { set.Enabled = !set.Enabled; Save(set); });
            Text(toggle, "● " + T(set.Enabled ? "active" : "paused"), 2, 0, 86, 17, 7, set.Enabled ? Good : Bad, TextAnchor.MiddleCenter);
            MetaSymbols.Tip(toggle, () => T(set.Enabled ? "rules_on" : "rules_off"), () =>
            {
                if (set.Enabled) return T("toggle_rules");
                var active = new RuleSet { Restrictions = set.Restrictions, Banned = set.Banned };
                int leaving = WorldRules.CountFailing(selected, active, out int total);
                return T("toggle_rules") + "\n\n" + Impact(leaving, total);
            });
            Small(block, "?", CW - 18, 30, 18, () => { ResetEditor(); help = true; Build(); }, T("help"));
        }

        static void ReturnToMeta() => NativeMetaWindows.OpenNative(selected);

        static void UpdateSummary()
        {
            if (summaryLabel == null) return;
            memberCount = WorldRules.MemberCount(selected, out incompatibleCount);
            summaryLabel.text = KindName(selected.Kind) + " · " + memberCount + " " + People.ToLowerInvariant() +
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
            if (IsEquipment) HelpBlock(T("equipment_intro"));
            Label(Block(22, "add_members"), "+  " + T(AddPeople), 0, 0, CW, 22,
                () => { ResetEditor(); members = true; addingMembers = true; Build(); });
            var diagnoses = new Dictionary<string, Diagnosis>();
            if (set.Enabled)
                foreach (Declaration d in set.Declarations)
                    if (d.Enabled) diagnoses[d.Id] = WorldRules.Diagnose(selected, d);
            NativeUi.Group declarations = Section(T("declarations"), set.Declarations.Count);
            foreach (Declaration value in set.Declarations)
            {
                Declaration item = value;
                RectTransform token = DeclarationToken(declarations.Grid, item, () => { effect = Clone(item); Build(); }, false);
                if (!item.Enabled || !set.Enabled) Fade(token);
                if (item.Kind == EffectKind.Stat) Badge(token, Number(item.Amount));
                else if (item.Lifetime == Lifetime.Permanent) Corner(token, Gold);
                if (diagnoses.TryGetValue(item.Id, out Diagnosis status) && Troubled(status)) Dot(token, StateColor(status.Worst));
            }
            Small(declarations.Grid, "+", 0, 0, Cell, () => { effect = new Declaration(); StartPicker(false); }, T("add_declaration"));
            if (set.Declarations.Count == 0) Hint(T("no_declarations") + " — " + T(IsEquipment ? "wearer_declarations_hint" : "declarations_hint"));
            StatusRows(set, diagnoses);

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

            int removed = set.Banned?.Count ?? 0;
            NativeUi.Group people = Section(People, memberCount);
            float peopleW = NativeUi.MakeList(people, 2f);
            Label(people.Grid, T(IsEquipment ? "manage_wearers" : "manage_members") + (removed > 0 ? "  ·  " + removed + " " + T("removed").ToLowerInvariant() : "") + "  ›",
                0, 0, peopleW, 18, () => { ResetEditor(); members = true; Build(); });
            Label(people.Grid, T("settings"), 0, 0, peopleW, 18,
                () => NeoModLoader.ui.ModConfigureWindow.ShowWindow(Main.Instance.GetConfig()));
        }

        static RectTransform DeclarationToken(Transform parent, Declaration d, Action click, bool marked)
        {
            RectTransform token = null;
            if (d.Kind == EffectKind.Trait && AssetManager.traits.get(d.Value) is ActorTrait trait)
                token = NativeUi.TraitButton(parent, trait, marked, click);
            else if (d.Kind == EffectKind.Item && AssetManager.items.get(d.Value) is EquipmentAsset item)
                token = NativeUi.ItemButton(parent, item, marked, click);
            else if (d.Kind == EffectKind.Meta && WorldRules.Find(d.Meta, d.MetaId) is MetaHandle meta)
            {
                token = NativeUi.Banner(parent, meta, Cell, click);
                MetaSymbols.Tip(token, () => DeclarationName(d), () => KindLabel(d));
                if (marked) Frame(token, 0);
            }
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
                case EffectKind.Meta: return MetaSymbols.KindSprite(d.Meta);
                default: return MetaSymbols.StatSprite(d.Value);
            }
        }

        static string DeclarationName(Declaration d)
        {
            switch (d.Kind)
            {
                case EffectKind.Trait: return TraitName(d.Value);
                case EffectKind.Item: return ItemName(d.Value);
                case EffectKind.Meta: return WorldRules.Find(d.Meta, d.MetaId)?.Name ?? T("missing_meta");
                default: return T("stat_" + d.Value) + " " + Number(d.Amount);
            }
        }

        static string KindLabel(Declaration d)
        {
            switch (d.Kind)
            {
                case EffectKind.Trait: return T("trait");
                case EffectKind.Item: return T("equipment") + " · " + SlotName(d.Value);
                case EffectKind.Meta: return T("grant") + " · " + KindName(d.Meta);
                default: return T("stat");
            }
        }

        static Declaration Clone(Declaration d) => new Declaration
        { Id = d.Id, Enabled = d.Enabled, Kind = d.Kind, Value = d.Value, Amount = d.Amount, Lifetime = d.Lifetime, Meta = d.Meta, MetaId = d.MetaId };
        static bool Chosen(Declaration d) => d.Kind == EffectKind.Meta ? d.MetaId >= 0 : !string.IsNullOrEmpty(d.Value);
        static Restriction Clone(Restriction r) => new Restriction
        {
            Id = r.Id, Enabled = r.Enabled, Any = r.Any,
            Conditions = r.Conditions.Select(c => new Condition { Kind = c.Kind, Trait = c.Trait, Meta = c.Meta, MetaId = c.MetaId, Negated = c.Negated }).ToList()
        };
        // The rule set as it would be after saving the declaration or requirement being edited.
        static RuleSet With(Declaration d) => new RuleSet
        { Restrictions = Current.Restrictions, Declarations = Current.Declarations.Where(x => x.Id != d.Id).Concat(new[] { d }).ToList() };
        static RuleSet With(Restriction r) => new RuleSet
        { Declarations = Current.Declarations, Restrictions = Current.Restrictions.Where(x => x.Id != r.Id).Concat(new[] { r }).ToList() };

        // Only clashes the edited entry takes part in: an older one elsewhere does not block it.
        static TraitClash ClashOf(Declaration d) => d.Kind != EffectKind.Trait ? null :
            RuleEngine.Clashes(With(d), WorldRules.Opposed).FirstOrDefault(c => c.Declared && (c.A == d.Value || c.Rule == null && c.B == d.Value));
        static TraitClash ClashOf(Restriction r) => RuleEngine.Clashes(With(r), WorldRules.Opposed).FirstOrDefault(c => c.Rule == r);
        static string ClashText(TraitClash c) => string.Format(T(c.Rule != null && c.Declared ? "clash_requirement" : "clash"),
            (c.ANegated ? T("not") + " " : "") + TraitName(c.A), (c.BNegated ? T("not") + " " : "") + TraitName(c.B));

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
            bool grant = effect.Kind == EffectKind.Meta;
            TraitClash clash = ClashOf(effect);
            float statusY = grant ? 66 : 88;
            float footerY = statusY + 32;
            RectTransform block = Block(footerY + 24, "declaration");
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
            else if (!grant)
            {
                Row(block, T("duration"), 66);
                Segment(block, x, 66, w, new[] { T(IsEquipment ? "while_equipped" : "while_member"), T("permanent") }, effect.Lifetime == Lifetime.Permanent ? 1 : 0,
                    i => effect.Lifetime = i == 1 ? Lifetime.Permanent : Lifetime.Membership);
            }
            Row(block, T("status"), statusY);
            Segment(block, x, statusY, w, new[] { T("enabled"), T("disabled") }, effect.Enabled ? 0 : 1, i => effect.Enabled = i == 0);
            Footer(block, footerY, () =>
            {
                if (!Chosen(effect) || clash != null) return;
                RuleSet set = Current;
                int index = set.Declarations.FindIndex(d => d.Id == effect.Id);
                if (index < 0) set.Declarations.Add(effect); else set.Declarations[index] = effect;
                effect = null; Save(set);
            }, () => { effect = null; Build(); }, exists ? (Action)(() =>
            { RuleSet set = Current; set.Declarations.RemoveAll(d => d.Id == effect.Id); effect = null; Save(set); }) : null);

            if (Chosen(effect))
            {
                Diagnosis status = WorldRules.Diagnose(selected, effect, Preview(effect));
                string note = grant && status.Preview && status.Applied > 0
                    ? string.Format(T("impact_join_leave"), KindName(effect.Meta).ToLowerInvariant()) : null;
                StatusCard(status, note);
            }
            if (clash != null) HelpBlock(ClashText(clash)).color = Bad;
            string helpText = effect.Kind == EffectKind.Trait ? "trait_help" : effect.Kind == EffectKind.Item ? "item_help" : grant ? "meta_help" : "stat_help";
            if (IsEquipment) HelpBlock(T("equipment_rules_help"));
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
            Row(head, T(IsEquipment ? "wearer_match" : "match"), 24);
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
            else Hint(T(draft.Conditions.Count == 0 ? IsEquipment ? "first_equipment_condition" : "first_condition" : "select_condition"));

            TraitClash clash = ClashOf(draft);
            if (clash != null) HelpBlock(ClashText(clash)).color = Bad;
            bool emptiesGroup = false;
            if (draft.Conditions.Count > 0)
            {
                RuleSet now = Current;
                var preview = new RuleSet
                {
                    Banned = now.Banned,
                    Restrictions = now.Restrictions.Where(r => r.Id != draft.Id).Concat(new[] { draft }).ToList()
                };
                int leaving = WorldRules.CountFailing(selected, preview, out int total);
                emptiesGroup = total > 0 && leaving == total;
                string text = Impact(leaving, total) + (now.Enabled ? "" : " " + T("impact_when_active"));
                Paragraph(Block(22, "impact"), text, 0, 0, CW, 22, 7, TextAnchor.MiddleCenter).color =
                    leaving == 0 ? Good : emptiesGroup ? Bad : Gold;
            }

            Footer(Block(20, "footer"), 0, () =>
            {
                if (draft.Conditions.Count == 0 || clash != null) return;
                if (emptiesGroup && !confirmEmpty) { confirmEmpty = true; Build(true); return; }
                RuleSet set = Current;
                int index = set.Restrictions.FindIndex(r => r.Id == draft.Id);
                if (index < 0) set.Restrictions.Add(draft); else set.Restrictions[index] = draft;
                draft = null; Save(set);
            }, () => { draft = null; Build(); }, exists ? (Action)(() =>
            { RuleSet set = Current; set.Restrictions.RemoveAll(r => r.Id == draft.Id); draft = null; Save(set); }) : null,
            confirmEmpty ? T(IsEquipment ? "confirm_unequip" : "confirm_empty") : null);
        }

        static string Impact(int leaving, int total) =>
            IsEquipment ? T(leaving == 0 ? "equipment_impact_none" : "equipment_impact_remove") :
            leaving == 0 ? T("impact_none") :
            leaving == total ? string.Format(T("impact_all"), total) :
            string.Format(T("impact_some"), leaving, total);

        static void BuildHelp()
        {
            EditorHeader(Block(18, "help"), T("help"), () => { help = false; Build(); });
            var parts = new List<string> { T(IsEquipment ? "equipment_rules_help" : "admission_help"), T("trait_help"), T("item_help"), T("stat_help"), T("provenance_help") };
            if (selected.Kind == MetaKind.Subspecies) parts.Add(T("subspecies_help"));
            foreach (string part in parts) HelpBlock(part);
        }

        // A wrapped paragraph whose block grows to fit its text.
        static Text HelpBlock(string text)
        {
            RectTransform block = Block(10, "paragraph");
            Text label = Paragraph(block, text, 2, 0, CW - 4, 10, 7, TextAnchor.UpperLeft);
            float height = Mathf.Ceil(label.preferredHeight) + 2;
            block.sizeDelta = new Vector2(CW, height);
            ((RectTransform)label.transform).sizeDelta = new Vector2(CW - 4, height);
            return label;
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
            picking = true; pickingCondition = forCondition; category = 0; query = ""; creationFeedback = "";
            Build();
        }

        static void LeavePicker()
        {
            picking = false;
            if (effect != null && !Chosen(effect)) effect = null;
            if (draft != null && draft.Conditions.Count == 0) draft = null;
            Build();
        }

        static void BuildPicker()
        {
            float y = ViewTop + 2f;
            string title = T(pickingCondition ? "add_condition" : effect != null && Chosen(effect) ? "change_selection" : "add_declaration");
            title += "  ·  " + (pickingCondition ? category == 0 ? T("traits") : KindName((MetaKind)(category - 1))
                : T(new[] { "traits", "equipment", "stats", "groups" }[Mathf.Clamp(category, 0, 3)]));
            Small(root, "‹", Left, y, 16, LeavePicker, T("back"));
            Text(root, Fit(title, 8, CW - 22), Left + 22, y, CW - 22, 16, 8, HeaderGold, TextAnchor.MiddleLeft);
            y += 19f;
            if (pickingCondition)
            {
                // Two rows keep every category legible as new rule sources are added.
                int tabs = 1 + Enum.GetValues(typeof(MetaKind)).Length;
                const int columns = 6;
                float tab = (CW - (columns - 1) * 2f) / columns;
                for (int i = 0; i < tabs; i++)
                {
                    int filter = i;
                    Sprite icon = i == 0 ? MetaSymbols.TraitSprite("strong") : MetaSymbols.KindSprite((MetaKind)(i - 1));
                    string label = i == 0 ? T("traits") : KindName((MetaKind)(i - 1));
                    RectTransform token = Symbol(root, Left + i % columns * (tab + 2f), y + i / columns * (tab + 2f), tab,
                        icon, null, () => SelectTab(filter), label);
                    if (category == i) Frame(token, 0);
                }
                y += Mathf.CeilToInt(tabs / (float)columns) * (tab + 2f);
            }
            else
            {
                string[] names = { T("traits"), T("equipment"), T("stats"), T("groups") };
                Sprite[] icons = { MetaSymbols.TraitSprite("strong"), MetaSymbols.EquipmentSprite(), MetaSymbols.StatSprite("damage"), MetaSymbols.KindSprite(MetaKind.Language) };
                float tab = (CW - 3 * 2f) / 4f;
                for (int i = 0; i < 4; i++)
                {
                    int filter = i;
                    string name = names[i];
                    RectTransform token = Button(root, Left + i * (tab + 2f), y, tab, 20, Slot, () => SelectTab(filter));
                    Picture(token, icons[i], tab / 2 - 7, 3, 14, 14);
                    MetaSymbols.Tip(token, () => name);
                    if (category == i) Frame(token, 0);
                }
                y += 23f;
            }
            Input(root, query, T("search_live"), Left, y, CW, 17, value => { query = value; Relayout(); });
            BuildResults();
        }

        static void SelectTab(int value)
        {
            if (category == value) return;
            category = value; query = ""; creationFeedback = "";
            Build();
        }

        static void BuildResults()
        {
            results = Choices();
            if (!string.IsNullOrEmpty(creationFeedback)) HelpBlock(creationFeedback);
            if (pickingCondition && category > 0 &&
                ((MetaKind)(category - 1) == MetaKind.City || (MetaKind)(category - 1) == MetaKind.Kingdom))
                HelpBlock(T("creation_territory"));
            if (IsEquipment && !pickingCondition && category == 1) HelpBlock(T("equipment_slot_help"));
            foreach (Group group in results)
            {
                group.View = NativeUi.CreateGroup(content, group.Title, group.Color);
                if (group.Banners) NativeUi.SetCellSize(group.View, new Vector2(34f, 40f));
                if (group.Rows) NativeUi.MakeList(group.View, 2f);
            }
            Hint(T("no_results")).name = "no_results";
            Hint(T("equipment_search_hint")).name = "equipment_search_hint";
            Relayout();
        }

        static void Relayout()
        {
            if (results == null) return;
            int total = 0;
            bool limited = false;
            foreach (Group group in results)
            {
                int shown = 0, matched = 0;
                foreach (Choice entry in group.Items)
                {
                    bool visible = entry.AlwaysVisible || Matches(entry.Search, query);
                    if (visible && group.Rows && shown >= MemberRows) { visible = false; limited = true; }
                    if (visible && entry.View == null) entry.View = entry.Create(group.View.Grid);
                    if (entry.View != null)
                    {
                        entry.View.gameObject.SetActive(visible);
                        if (visible) entry.View.SetAsLastSibling();
                    }
                    if (visible) { shown++; if (!entry.AlwaysVisible) matched++; }
                }
                group.View.Root.gameObject.SetActive(shown > 0);
                if (group.View.Counter != null) group.View.Counter.text = matched.ToString();
                total += matched;
            }
            content.Find("no_results")?.gameObject.SetActive(total == 0);
            content.Find("equipment_search_hint")?.gameObject.SetActive(limited);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            content.anchoredPosition = Vector2.zero;
        }

        static List<Group> Choices()
        {
            if (pickingCondition && category > 0) return MetaChoices((MetaKind)(category - 1));
            if (category == 0) return TraitChoices();
            if (category == 1) return ItemChoices();
            if (category == 3) return GrantChoices();
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
                TraitClash clash = PickClash(entry.id);
                Action select = clash != null ? () => { creationFeedback = ClashText(clash); Build(); } : (Action)(() =>
                {
                    if (pickingCondition) { draft.Conditions.Add(new Condition { Trait = entry.id }); selectedCondition = -1; }
                    else SetEffect(EffectKind.Trait, entry.id);
                    picking = false; Build();
                });
                group.Items.Add(new Choice
                {
                    Search = name + " " + entry.id,
                    Create = grid =>
                    {
                        RectTransform token = NativeUi.TraitButton(grid, entry, marked, select)
                            ?? Marked(Symbol(grid, 0, 0, Cell, entry.getSprite(), null, select, name, group.Title), marked);
                        if (clash != null) Fade(token);
                        return token;
                    }
                });
            }
            groups.Add(other);
            return groups;
        }

        // What choosing this trait in the picker would clash with.
        static TraitClash PickClash(string trait)
        {
            if (pickingCondition)
            {
                Restriction next = Clone(draft);
                next.Conditions.Add(new Condition { Trait = trait });
                return RuleEngine.Clashes(With(next), WorldRules.Opposed).FirstOrDefault(c => c.Rule == next && c.Involves(trait));
            }
            Declaration choice = effect != null ? Clone(effect) : new Declaration();
            choice.Kind = EffectKind.Trait; choice.Value = trait;
            return ClashOf(choice);
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
                // Replacing the source item would immediately end its own grant.
                if (IsEquipment && selected.Target is Item source && item.equipment_type == source.getAsset().equipment_type) continue;
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
            bool equipment = type == MetaKind.Equipment;
            var group = new Group { Title = KindName(type), Banners = !equipment, Rows = equipment };
            if (!equipment) AddCreationChoices(group, type);
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
                group.Items.Add(new Choice
                {
                    Search = meta.Name + " #" + meta.Id + " " + (equipment ? EquipmentLocation((Item)meta.Target) : ""),
                    Create = grid => equipment ? EquipmentChoice(grid, meta, marked, select) : Marked(NativeUi.Banner(grid, meta, 34, select), marked)
                });
            }
            return new List<Group> { group };
        }

        static void AddCreationChoices(Group group, MetaKind kind)
        {
            if (kind == MetaKind.Army)
            {
                Kingdom scope = WorldRules.KingdomOf(selected);
                bool any = false;
                foreach (City city in World.world.cities)
                    if (city.isAlive() && city.kingdom != null && city.kingdom.isAlive() && !city.hasArmy() &&
                        (scope == null || city.kingdom == scope) && (!(selected.Target is City sourceCity) || sourceCity == city))
                    { AddCreationChoice(group, kind, city); any = true; }
                if (!any) AddCreationChoice(group, kind);
                return;
            }
            // Territory-based metas cannot be made valid merely by adding an empty object.
            if (MetaCreation.For(kind) != null) AddCreationChoice(group, kind);
        }

        static void AddCreationChoice(Group group, MetaKind kind, City city = null)
        {
            group.Items.Add(new Choice
            {
                AlwaysVisible = true,
                Create = grid =>
                {
                    string title = "+ " + string.Format(T("create_meta"), KindName(kind));
                    string problem = MetaCreation.Problem(kind, selected, city);
                    RectTransform button = Button(grid, 0, 0, 34, 40, Slot, () => CreateMeta(kind, city));
                    Text(button, "+", 0, 0, 34, 28, 20, problem == null ? Good : Muted, TextAnchor.MiddleCenter);
                    Picture(button, MetaSymbols.KindSprite(kind), 23, 2, 9, 9);
                    if (city != null) Text(button, Fit(city.name, 5, 32), 1, 28, 32, 10, 5, Ink, TextAnchor.MiddleCenter);
                    button.GetComponent<Button>().interactable = problem == null;
                    MetaSymbols.Tip(button, () => title, () => (city != null ? city.name + " · " + city.kingdom?.name + "\n\n" : "") +
                        T(problem ?? "create_meta_help"));
                    return button;
                }
            });
        }

        static void CreateMeta(MetaKind kind, City city)
        {
            if (!picking || !ReferenceEquals(world, World.world?.map_stats)) return;
            try
            {
                MetaHandle made = MetaCreation.Create(kind, selected, city);
                if (made == null)
                {
                    creationFeedback = T(MetaCreation.Problem(kind, selected, city) ?? "creation_unavailable");
                    Build(); return;
                }
                if (pickingCondition)
                {
                    draft.Conditions.Add(new Condition { Kind = ConditionKind.Membership, Meta = made.Kind, MetaId = made.Id });
                    selectedCondition = -1;
                }
                else
                {
                    SetEffect(EffectKind.Meta, "");
                    effect.Meta = made.Kind; effect.MetaId = made.Id;
                }
                picking = false; creationFeedback = ""; createdMeta = made;
                NativeMetaWindows.OpenNative(made);
            }
            catch (Exception error)
            {
                WorldRules.Report(error);
                creationFeedback = T("creation_failed"); Build();
            }
        }

        static string EquipmentLocation(Item item) => item.getActor() != null ? item.getActor().getName() : item.getCity()?.name ?? T("no_wearer");

        static RectTransform EquipmentChoice(Transform grid, MetaHandle meta, bool marked, Action select)
        {
            float width = Mathf.Min(CW, ((RectTransform)grid).rect.width > 1 ? ((RectTransform)grid).rect.width : CW) - 8;
            RectTransform row = Button(grid, 0, 0, width, 34, Slot, select);
            MetaSymbols.Draw(row, meta, 3, 3, 28);
            Text(row, Fit(meta.Name, 7, width - 38), 35, 2, width - 38, 15, 7, Ink, TextAnchor.MiddleLeft);
            string detail = "#" + meta.Id + " · " + EquipmentLocation((Item)meta.Target);
            Text(row, Fit(detail, 6, width - 38), 35, 17, width - 38, 14, 6, Muted, TextAnchor.MiddleLeft);
            MetaSymbols.Tip(row, () => meta.Name, () => detail);
            return Marked(row, marked);
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
            effect.Kind = kind; effect.Value = value; effect.MetaId = -1;
        }

        // Groups members are made to join; a meta never grants its own type. Cities and armies
        // are listed for the selected meta's kingdom, or grouped per kingdom when it has none.
        static readonly MetaKind[] GrantKinds =
        { MetaKind.Language, MetaKind.Culture, MetaKind.Religion, MetaKind.Clan, MetaKind.Kingdom, MetaKind.City, MetaKind.Army };

        static List<Group> GrantChoices()
        {
            var groups = new List<Group>();
            Kingdom scope = WorldRules.KingdomOf(selected);
            foreach (MetaKind type in GrantKinds)
            {
                if (type == selected.Kind) continue;
                // Moving an army's or city's members elsewhere would only take them out of it.
                if (selected.Kind == MetaKind.Army && (type == MetaKind.City || type == MetaKind.Kingdom)) continue;
                if (selected.Kind == MetaKind.City && type == MetaKind.Kingdom) continue;
                bool perKingdom = type == MetaKind.City || type == MetaKind.Army;
                var byKingdom = new Dictionary<long, Group>();
                Group plain = null;
                if ((!perKingdom && MetaCreation.For(type) != null) || type == MetaKind.Army)
                {
                    plain = new Group { Title = KindName(type), Banners = true };
                    AddCreationChoices(plain, type);
                    groups.Add(plain);
                }
                foreach (MetaHandle value in WorldRules.Metas)
                {
                    MetaHandle meta = value;
                    if (meta.Kind != type) continue;
                    Kingdom owner = perKingdom ? WorldRules.KingdomOf(meta) : null;
                    if (perKingdom && scope != null && owner != scope) continue;
                    Group group;
                    if (!perKingdom || scope != null)
                    {
                        if (plain == null) groups.Add(plain = new Group { Title = KindName(type), Banners = true });
                        group = plain;
                    }
                    else
                    {
                        long key = owner?.id ?? -1;
                        if (!byKingdom.TryGetValue(key, out group))
                        {
                            group = new Group { Title = KindName(type) + " · " + (owner?.name ?? T("other")), Banners = true };
                            byKingdom[key] = group;
                            groups.Add(group);
                        }
                    }
                    bool marked = Current.Declarations.Any(d => d.Kind == EffectKind.Meta && d.Meta == meta.Kind && d.MetaId == meta.Id);
                    Action select = () =>
                    {
                        SetEffect(EffectKind.Meta, "");
                        effect.Meta = meta.Kind; effect.MetaId = meta.Id;
                        picking = false; Build();
                    };
                    group.Items.Add(new Choice { Search = meta.Name + " " + (owner?.name ?? ""), Create = grid => Marked(NativeUi.Banner(grid, meta, 34, select), marked) });
                }
            }
            return groups;
        }

        // ── Members: add or remove individuals, reusing the same admission planner.
        const int MemberRows = 200;

        static void BuildMembers()
        {
            RectTransform head = Block(addingMembers ? 86 : 64, "members");
            EditorHeader(head, People, () => { members = false; Build(); });
            Segment(head, 0, 22, CW, new[] { People, T(AddPeople) }, addingMembers ? 1 : 0,
                i => { addingMembers = i == 1; memberQuery = ""; memberFeedback = ""; });
            if (addingMembers) Segment(head, 0, 44, CW, new[] { T("all_candidates"), T("eligible_candidates") }, onlyEligible ? 1 : 0,
                i => onlyEligible = i == 1);
            Input(head, memberQuery, T("search_name_id"), 0, addingMembers ? 66 : 44, CW, 17, value => { memberQuery = value; FillMembers(); });
            if (!string.IsNullOrEmpty(memberFeedback)) HelpBlock(memberFeedback);
            memberList = NativeUi.CreateGroup(content, addingMembers ? T(AddPeople) : People, HeaderGold);
            NativeUi.MakeList(memberList, 2f);
            FillMembers();

            RuleSet set = Current;
            if (set.Banned != null && set.Banned.Count > 0)
            {
                NativeUi.Group removed = Section(T("removed"), set.Banned.Count);
                float rowW = NativeUi.MakeList(removed, 2f);
                foreach (long value in set.Banned.ToList())
                {
                    long id = value;
                    Actor actor = World.world.units.get(id);
                    RectTransform row = Sliced(removed.Grid, Slot, 0, 0, rowW, 22);
                    if (actor != null && actor.isAlive()) Avatar(row, actor);
                    string name = actor != null && actor.isAlive() ? actor.getName() : "#" + id + " · " + T("unit_dead");
                    Text(row, Fit(name, 7, rowW - 86), 22, 0, rowW - 86, 22, 7, Muted, TextAnchor.MiddleLeft);
                    Label(row, T("readmit"), rowW - 62, 2, 60, 18, () => { WorldRules.Readmit(selected, id); Build(); });
                }
                if (!set.Enabled) Hint(T("removed_paused"));
            }
            HelpBlock(T(IsEquipment ? "wearers_help" : "members_help"));
        }

        static void FillMembers()
        {
            if (memberList == null) return;
            Clear(memberList.Grid);
            List<Actor> all = addingMembers
                ? World.world.units.getSimpleList().Where(a => a != null && a.isAlive() && !WorldRules.Belongs(a, selected)).ToList()
                : WorldRules.Members(selected);
            var matching = all.Where(a => Matches((a.getName() ?? "") + " #" + a.id, memberQuery) &&
                (!addingMembers || !onlyEligible || WorldRules.CanAdd(selected, a))).OrderBy(a => a.getName()).ThenBy(a => a.id).ToList();
            if (memberList.Counter != null) memberList.Counter.text = matching.Count.ToString();
            float rowW = Mathf.Min(CW, ((RectTransform)memberList.Grid).rect.width > 1 ? ((RectTransform)memberList.Grid).rect.width : CW) - 8;
            foreach (Actor value in matching.Take(MemberRows))
            {
                Actor actor = value;
                RectTransform row = Sliced(memberList.Grid, Slot, 0, 0, rowW, 22);
                Avatar(row, actor);
                var issues = addingMembers ? null : WorldRules.Issues(selected, actor).Where(i => i.Value.State > Standing.Pending).ToList();
                float nameW = rowW - 86 - (issues?.Count > 0 ? 20 : 0);
                RectTransform open = Button(row, 22, 0, nameW, 22, null, () => { SelectedUnit.select(actor); ScrollWindow.showWindow("unit"); });
                Text(open, Fit(actor.getName() ?? "#" + actor.id, 7, nameW), 0, 0, nameW, 22, 7, Ink, TextAnchor.MiddleLeft);
                if (issues?.Count > 0) IssueChip(row, rowW - 84, actor, issues);
                MetaSymbols.Tip(open, () => actor.getName() + " #" + actor.id, () => T("open_unit"));
                if (addingMembers)
                {
                    bool allowed = WorldRules.CanAdd(selected, actor);
                    RectTransform add = Label(row, T("add"), rowW - 62, 2, 60, 18, () =>
                    {
                        memberFeedback = WorldRules.TryAdd(selected, actor) ? Added(actor) : AdditionProblem(actor);
                        Build();
                    });
                    add.GetComponent<UnityEngine.UI.Button>().interactable = allowed;
                    if (!allowed)
                    {
                        Fade(add);
                        MetaSymbols.Tip(add, () => actor.getName() + " #" + actor.id, () => AdditionProblem(actor));
                    }
                }
                else Label(row, T(IsEquipment ? "unequip" : "remove"), rowW - 62, 2, 60, 18, () => { WorldRules.Remove(selected, actor); Build(); }, Red);
            }
            if (matching.Count > MemberRows)
                Text(Box(memberList.Grid, 0, 0, rowW, 16, "more"), string.Format(T("showing"), MemberRows, matching.Count), 0, 0, rowW, 16, 6, Muted, TextAnchor.MiddleCenter);
            else if (matching.Count == 0)
                Text(Box(memberList.Grid, 0, 0, rowW, 16, "none"), T("no_results"), 0, 0, rowW, 16, 6, Muted, TextAnchor.MiddleCenter);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        static string AdditionProblem(Actor actor) => Why(WorldRules.AdditionProblem(selected, actor));

        // A new wearer is told which of its groups' traits the item now overrides.
        static string Added(Actor actor)
        {
            if (!IsEquipment) return string.Format(T("member_added_named"), actor.getName());
            string text = string.Format(T("wearer_added_named"), actor.getName());
            var overrides = WorldRules.Overrides(selected, actor);
            if (overrides.Count == 0) return text;
            return text + " " + string.Format(T("wearer_overrides"), string.Join(", ", overrides.Select(p => TraitName(p.Value) + " > " + TraitName(p.Key))));
        }

        // ── Status: what each declaration actually does for the members, and why not.
        static Color StateColor(Standing state) =>
            state == Standing.Applied ? Good : state == Standing.Blocked ? Bad : state == Standing.Pending ? Muted : Gold;

        static string Bullet(Standing state) => "<color=" + Tag(StateColor(state)) + ">●</color> ";

        static string Why(Finding f)
        {
            string who = f.Name == null ? "" : f.Kind.HasValue ? KindName(f.Kind.Value) + " " + f.Name : f.Name;
            string rule = f.Rule == null ? "" : string.Join(f.Rule.Any ? " " + T("or") + " " : " + ", f.Rule.Conditions.Select(Describe));
            string text = string.Format(T("why_" + f.Reason), who, f.Trait == null ? "" : TraitName(f.Trait), rule, f.Other ?? "");
            return text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);
        }

        static string Headline(Diagnosis status)
        {
            if (status.Members == 0) return T(IsEquipment ? "diag_no_wearer" : "diag_no_members");
            string text = IsEquipment && status.Checked == 1
                ? T(status.Missing == 0 ? status.Preview ? "diag_would_wearer" : "diag_wearer" : status.Preview ? "diag_would_not_wearer" : "diag_not_wearer")
                : status.Missing == 0 ? string.Format(T(status.Preview ? "diag_would_all" : "diag_all"), status.Checked)
                : string.Format(T(status.Preview ? "diag_would" : "diag_applies"), status.Applied, status.Checked);
            if (status.Checked < status.Members) text += " " + string.Format(T("diag_sampled"), status.Checked, status.Members);
            return text;
        }

        // Something keeps the declaration from a member; a grant the next scan applies is not.
        static bool Troubled(Diagnosis status) => status.Issues.Any(i => i.Key.State > Standing.Pending);

        static string Reasons(Diagnosis status) =>
            string.Join("\n", status.Issues.Select(i => Bullet(i.Key.State) + i.Value + " · " + Why(i.Key)));

        // The rule set as it would be after saving this declaration; null when it is saved as is.
        static RuleSet Preview(Declaration d)
        {
            RuleSet now = Current;
            Declaration saved = now.Declarations.FirstOrDefault(x => x.Id == d.Id);
            if (saved != null && saved.Enabled == d.Enabled && saved.Kind == d.Kind && saved.Value == d.Value && saved.Amount == d.Amount &&
                saved.Lifetime == d.Lifetime && saved.Meta == d.Meta && saved.MetaId == d.MetaId) return null;
            return new RuleSet
            {
                Enabled = now.Enabled, Banned = now.Banned, Restrictions = now.Restrictions,
                Declarations = now.Declarations.Where(x => x.Id != d.Id).Concat(new[] { d }).ToList()
            };
        }

        // Sizes a wrapped paragraph to its text and returns its height.
        static float Grow(Text text, float w)
        {
            float height = Mathf.Ceil(text.preferredHeight);
            ((RectTransform)text.transform).sizeDelta = new Vector2(w, height);
            return height;
        }

        // A thin bar split by state: applied first, then each reason in proportion to its count.
        static void StatusBar(Transform parent, Diagnosis status, float x, float y, float w)
        {
            Rect(parent, x, y, w, 5, Edge);
            if (status.Checked == 0) return;
            float cursor = x + 1, inner = w - 2;
            void Part(int count, Color color)
            {
                float width = inner * count / status.Checked;
                if (width > 0) Rect(parent, cursor, y + 1, width, 3, color);
                cursor += width;
            }
            Part(status.Applied, Good);
            foreach (var issue in status.Issues) Part(issue.Value, StateColor(issue.Key.State));
        }

        const int ShownReasons = 5;

        static void StatusCard(Diagnosis status, string note)
        {
            RectTransform block = Block(10, "status");
            RectTransform card = Sliced(block, Slot, 0, 0, CW, 10);
            Color color = status.Members == 0 ? Muted : StateColor(status.Missing == 0 ? Standing.Applied : status.Worst);
            Text(card, T("diag_title"), 6, 4, CW - 12, 10, 6, HeaderGold, TextAnchor.MiddleLeft);
            if (status.Checked > 0) Text(card, status.Applied + "/" + status.Checked, 6, 4, CW - 12, 10, 7, color, TextAnchor.MiddleRight);
            float y = 16;
            Text headline = Paragraph(card, Headline(status), 6, y, CW - 12, 10, 7);
            headline.color = color;
            y += Grow(headline, CW - 12) + 3;
            if (status.Checked > 0) { StatusBar(card, status, 6, y, CW - 12); y += 9; }
            for (int i = 0; i < status.Issues.Count && i < ShownReasons; i++)
            {
                var issue = status.Issues[i];
                Rect(card, 7, y + 3, 4, 4, Edge);
                Rect(card, 8, y + 4, 2, 2, StateColor(issue.Key.State));
                Text line = Paragraph(card, "<color=" + Tag(Ink) + ">" + issue.Value + "</color> · " + Why(issue.Key), 15, y, CW - 21, 10, 6);
                y += Grow(line, CW - 21) + 2;
            }
            if (status.Issues.Count > ShownReasons)
            {
                Text more = Paragraph(card, string.Format(T("diag_more"), status.Issues.Count - ShownReasons), 15, y, CW - 21, 10, 6);
                y += Grow(more, CW - 21) + 2;
            }
            if (note != null)
            {
                Text extra = Paragraph(card, note, 6, y + 1, CW - 12, 10, 6);
                y += Grow(extra, CW - 12) + 3;
            }
            y += 4;
            block.sizeDelta = card.sizeDelta = new Vector2(CW, y);
        }

        // Declarations that do not reach every member, with the main reason; click to edit.
        static void StatusRows(RuleSet set, Dictionary<string, Diagnosis> diagnoses)
        {
            var failing = set.Declarations.Where(d => diagnoses.TryGetValue(d.Id, out Diagnosis status) && Troubled(status)).ToList();
            if (failing.Count == 0) return;
            NativeUi.Group group = Section(T("diag_section"), failing.Count);
            float rowW = NativeUi.MakeList(group, 2f);
            foreach (Declaration value in failing)
            {
                Declaration d = value;
                Diagnosis status = diagnoses[d.Id];
                Color color = StateColor(status.Worst);
                RectTransform row = Button(group.Grid, 0, 0, rowW, 28, Slot, () => { effect = Clone(d); Build(); });
                Rect(row, 3, 4, 2, 20, color);
                if (d.Kind == EffectKind.Meta && WorldRules.Find(d.Meta, d.MetaId) is MetaHandle meta) MetaSymbols.Draw(row, meta, 8, 4, 20);
                else Picture(row, DeclarationSprite(d), 9, 5, 18, 18);
                string count = status.Applied + "/" + status.Checked;
                float countW = Measure(count, 7) + 4, textW = rowW - 36;
                Text(row, Fit(DeclarationName(d), 7, textW - countW - 2), 32, 3, textW - countW - 2, 12, 7, Ink, TextAnchor.MiddleLeft);
                Text(row, count, rowW - countW - 5, 3, countW, 12, 7, color, TextAnchor.MiddleRight);
                string reason = Why(status.Issues[0].Key) + (status.Issues.Count > 1 ? "  +" + (status.Issues.Count - 1) : "");
                Text(row, Fit(reason, 6, textW), 32, 15, textW, 10, 6, Muted, TextAnchor.MiddleLeft);
                MetaSymbols.Tip(row, () => DeclarationName(d), () => Headline(status) + "\n\n" + Reasons(status) + "\n\n" + T("diag_open"));
            }
        }

        static void IssueChip(RectTransform row, float x, Actor actor, List<KeyValuePair<Declaration, Finding>> issues)
        {
            Standing worst = issues.Max(i => i.Value.State);
            RectTransform chip = Button(row, x, 2, 18, 18, Slot, () => { });
            Text(chip, "!", 0, 0, 18, 18, 9, StateColor(worst), TextAnchor.MiddleCenter);
            MetaSymbols.Tip(chip, () => string.Format(T("diag_member_issues"), actor.getName()),
                () => string.Join("\n", issues.Select(i => Bullet(i.Value.State) + DeclarationName(i.Key) + ": " + Why(i.Value))));
        }

        static void Dot(RectTransform token, Color color)
        {
            Rect(token, 1, 1, 8, 8, Edge);
            Rect(token, 2, 2, 6, 6, color);
        }

        static void Avatar(RectTransform row, Actor actor)
        {
            Sprite sprite = null;
            try { sprite = actor.asset?.getSpriteIcon(); } catch (Exception) { /* Missing icon. */ }
            if (sprite != null) Picture(row, sprite, 3, 3, 16, 16);
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
                MetaSymbols.Tip(button, () => options[index]);
                if (i == active) Frame(button, 0);
            }
        }

        static void Footer(Transform parent, float y, Action save, Action cancel, Action remove, string confirm = null)
        {
            if (confirm != null)
            {
                // Destructive save: one wide red button asks for the second click.
                Label(parent, T("cancel"), 0, y, 60, 20, cancel);
                Label(parent, confirm, 64, y, CW - 64, 20, save, Red);
                return;
            }
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

    // The native Back button can reopen the rules window through window history.
    // Rebuild on activation too, so it shows the edited name/banner and the draft.
    public sealed class RulesWindowResume : MonoBehaviour
    {
        void OnEnable() => RulesWindow.ResumeAfterNativeEditor();
    }
}
