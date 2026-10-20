using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MetaRules.Core;
using MetaRules.Game;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;
using static MetaRules.UI.UiKit;
using Object = UnityEngine.Object;

namespace MetaRules.UI
{
    // Native ScrollWindow and sliced sprites, following Diplomacy Overhaul's UI idiom.
    public static class RulesWindow
    {
        const string WindowId = "mr_window";
        const float W = 540f, H = 350f, Left = 160f, Right = 354f;
        static ScrollWindow window;
        static RectTransform root;
        static MetaKind kind;
        static MetaHandle selected;
        static int tab;
        static string query = "";
        static Restriction draft;
        static Declaration effect;
        static Action<ActorTrait> pickTrait;
        static Action<MetaHandle> pickMeta;
        static Action<string> pickStat;
        static MetaKind pickKind;
        static string pickQuery = "";
        static int page;
        static MapStats world;

        public static string T(string key) => LM.Get("mr_" + key);
        public static string KindName(MetaKind value) => T("kind_" + value.ToString().ToLowerInvariant());
        static string TraitName(string id)
        {
            string key = "trait_" + id;
            return LocalizedTextManager.stringExists(key) ? LM.Get(key) : id;
        }

        public static void Init()
        {
            PowersTab powers = TabManager.CreateTab("mr_tab", "mr_tab", "mr_tab_description", SpriteTextureLoader.getSprite("ui/Icons/iconClan"));
            powers.parentObj = powers.transform.parent.parent.gameObject;
            PowerButtonCreator.AddButtonToTab(PowerButtonCreator.CreateSimpleButton("mr_open", Open,
                SpriteTextureLoader.getSprite("ui/Icons/iconOptions")), powers);
        }

        public static void Open()
        {
            if (World.world?.units == null || SmoothLoader.isLoading()) return;
            if (!ReferenceEquals(world, World.world.map_stats))
            {
                world = World.world.map_stats;
                selected = null; draft = null; effect = null; ClearPicker(); query = "";
            }
            WorldRules.RefreshMetas();
            if (window == null) Create();
            ScrollWindow.showWindow(WindowId);
            Build();
            FitWindow();
        }

        // Called from the native window: keep the exact selected instance, not its type.
        public static void OpenFor(MetaHandle meta)
        {
            Open();
            if (meta == null || window == null) return;
            selected = meta; kind = meta.Kind; query = "";
            draft = null; effect = null; ClearPicker();
            Build();
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
            window.titleText.transform.localPosition = new Vector3(0f, H / 2f - 2f);
            root = Box(bg, 0, 0, W, H, "mr_root");
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
        }

        static void FitWindow()
        {
            Canvas canvas = window.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            RectTransform rect = canvas.transform as RectTransform;
            float scale = Mathf.Min(1f, (rect.rect.width - 80f) / (W + 16f), (rect.rect.height - 35f) / (H + 20f));
            window.transform.localScale = new Vector3(Mathf.Max(0.35f, scale), Mathf.Max(0.35f, scale), 1f);
        }

        static void ClearPicker() { pickTrait = null; pickMeta = null; pickStat = null; page = 0; pickQuery = ""; }
        static RuleSet Current => WorldRules.Get(selected) ?? new RuleSet();
        static void Save(RuleSet set) { WorldRules.Save(selected, set); Build(); }

        static void Build()
        {
            if (root == null) return;
            foreach (Transform child in root)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }
            // Targets may die while the window is open; never edit a stale world's data.
            if (selected != null && !WorldRules.Metas.Any(m => m.Target == selected.Target && m.Id == selected.Id))
            { selected = null; draft = null; effect = null; ClearPicker(); }
            BuildBrowser();
            RectTransform pane = Box(root, Left + 16f, 22f, Right, H - 45f);
            if (selected == null)
            {
                Header(pane, 0, 0, Right, T("welcome"), "ui/Icons/iconClan");
                Paragraph(pane, T("intro"), 8, 32, Right - 16, 100);
                Paragraph(pane, T("admission_help"), 8, 145, Right - 16, 95);
                return;
            }
            Header(pane, 0, 0, Right, Fit(selected.Name, 9, Right - 55), "ui/Icons/iconClan");
            Text(pane, KindName(selected.Kind) + " · #" + selected.Id, 7, 22, Right - 14, 12, 6, Muted, TextAnchor.MiddleLeft);
            if (WorldRules.IsBroken(selected)) { Paragraph(pane, T("invalid_data"), 8, 45, Right - 16, 100); return; }
            if (pickTrait != null || pickMeta != null || pickStat != null) { BuildPicker(pane); return; }
            if (draft != null) { BuildRestriction(pane); return; }
            if (effect != null) { BuildDeclaration(pane); return; }
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                LabelButton(pane, T(new[] { "restrict", "declare", "inspect" }[i]), i * 118, 38, 114, 19,
                    () => { tab = index; Build(); }, tab == i);
            }
            if (tab == 0) BuildRestrictions(pane);
            else if (tab == 1) BuildDeclarations(pane);
            else BuildInspection(pane);
        }

        static void BuildBrowser()
        {
            RectTransform browser = Sliced(root, Slot, 8, 22, Left, H - 45);
            foreach (MetaKind value in Enum.GetValues(typeof(MetaKind)))
            {
                MetaKind captured = value;
                int i = (int)value;
                LabelButton(browser, KindName(value), 4 + i % 3 * 51, 5 + i / 3 * 19, 49, 17,
                    () => { kind = captured; query = ""; Build(); }, kind == value);
            }
            Input(browser, query, T("search"), 5, 67, Left - 10, s => { query = s; Build(); });
            var metas = WorldRules.Metas.Where(m => m.Kind == kind && Matches(m.Name, query)).ToList();
            RectTransform list = Scroll(browser, "metas", 4, 91, Left - 8, 185, metas.Count * 25f);
            for (int i = 0; i < metas.Count; i++)
            {
                MetaHandle meta = metas[i];
                bool active = selected?.Target == meta.Target;
                RectTransform row = LabelButton(list, Fit(meta.Name, 7, Left - 20), 0, i * 25, Left - 8, 23, () =>
                { selected = meta; draft = null; effect = null; ClearPicker(); Build(); }, active);
                RuleSet set = WorldRules.Get(meta);
                if (set != null) Text(row, set.Restrictions.Count + " / " + set.Declarations.Count, 5, 14, Left - 18, 7, 5, Muted, TextAnchor.LowerRight);
            }
            LabelButton(browser, T("refresh"), 5, 282, Left - 10, 17, () => { WorldRules.RefreshMetas(); Build(); });
        }

        static void BuildRestrictions(RectTransform pane)
        {
            RuleSet set = Current;
            LabelButton(pane, "+ " + T("restriction"), 0, 65, Right, 20, () => { draft = new Restriction(); Build(); });
            Paragraph(pane, T("restriction_hint"), 5, 89, Right - 10, 30);
            RectTransform list = Scroll(pane, "restrictions", 0, 123, Right, 150, set.Restrictions.Count * 45f);
            for (int i = 0; i < set.Restrictions.Count; i++)
            {
                Restriction rule = set.Restrictions[i];
                RectTransform card = Sliced(list, Slot, 0, i * 45, Right, 42);
                Text(card, (rule.Any ? T("any") : T("all")) + ": " + rule.Conditions.Count, 6, 2, Right - 110, 13, 7, Gold, TextAnchor.MiddleLeft);
                Text(card, Fit(string.Join("; ", rule.Conditions.Select(Describe)), 6, Right - 18), 6, 17, Right - 12, 14, 6, Ink, TextAnchor.MiddleLeft);
                LabelButton(card, T(rule.Enabled ? "on" : "off"), Right - 105, 2, 32, 13, () => { rule.Enabled = !rule.Enabled; Save(set); }, rule.Enabled);
                LabelButton(card, T("edit"), Right - 70, 2, 32, 13, () => { draft = Clone(rule); Build(); });
                LabelButton(card, "×", Right - 34, 2, 28, 13, () => { set.Restrictions.Remove(rule); Save(set); });
            }
        }

        static Restriction Clone(Restriction r) => new Restriction
        {
            Id = r.Id, Enabled = r.Enabled, Any = r.Any,
            Conditions = r.Conditions.Select(c => new Condition { Kind = c.Kind, Trait = c.Trait, Meta = c.Meta, MetaId = c.MetaId, Negated = c.Negated }).ToList()
        };

        static string Describe(Condition condition)
        {
            string label = condition.Kind == ConditionKind.Trait ? TraitName(condition.Trait) :
                KindName(condition.Meta) + ": " + (WorldRules.Metas.FirstOrDefault(m => m.Kind == condition.Meta && m.Id == condition.MetaId)?.Name ?? "#" + condition.MetaId);
            return (condition.Negated ? T("not") + " " : "") + label;
        }

        static void BuildRestriction(RectTransform pane)
        {
            LabelButton(pane, T(draft.Any ? "any" : "all"), 0, 40, Right, 19, () => { draft.Any = !draft.Any; Build(); });
            LabelButton(pane, "+ " + T("trait"), 0, 64, 170, 20, () =>
            { pickTrait = t => { draft.Conditions.Add(new Condition { Trait = t.id }); ClearPicker(); Build(); }; Build(); });
            LabelButton(pane, "+ " + T("membership"), 178, 64, 176, 20, () =>
            {
                pickMeta = m => { draft.Conditions.Add(new Condition { Kind = ConditionKind.Membership, Meta = m.Kind, MetaId = m.Id }); ClearPicker(); Build(); };
                pickKind = MetaKind.Clan; Build();
            });
            RectTransform list = Scroll(pane, "conditions", 0, 94, Right, 140, draft.Conditions.Count * 27);
            for (int i = 0; i < draft.Conditions.Count; i++)
            {
                Condition c = draft.Conditions[i];
                Text(list, Fit(Describe(c), 7, Right - 85), 4, i * 27 + 3, Right - 85, 17, 7, Ink, TextAnchor.MiddleLeft);
                LabelButton(list, T("not"), Right - 75, i * 27, 40, 21, () => { c.Negated = !c.Negated; Build(); }, c.Negated);
                LabelButton(list, "×", Right - 29, i * 27, 25, 21, () => { draft.Conditions.Remove(c); Build(); });
            }
            LabelButton(pane, T("cancel"), 0, 250, 170, 22, () => { draft = null; Build(); });
            LabelButton(pane, T("save"), 178, 250, 176, 22, () =>
            {
                if (draft.Conditions.Count == 0) return;
                RuleSet set = Current;
                set.Restrictions.RemoveAll(r => r.Id == draft.Id);
                set.Restrictions.Add(draft); draft = null; Save(set);
            }, true);
        }

        static void BuildDeclarations(RectTransform pane)
        {
            RuleSet set = Current;
            LabelButton(pane, "+ " + T("trait"), 0, 65, 170, 20, () =>
            { effect = new Declaration(); pickTrait = t => { effect.Value = t.id; ClearPicker(); Build(); }; Build(); });
            LabelButton(pane, "+ " + T("stat"), 178, 65, 176, 20, () =>
            { effect = new Declaration { Kind = EffectKind.Stat, Amount = 10 }; pickStat = s => { effect.Value = s; ClearPicker(); Build(); }; Build(); });
            Paragraph(pane, T("declaration_hint"), 5, 89, Right - 10, 30);
            RectTransform list = Scroll(pane, "declarations", 0, 123, Right, 150, set.Declarations.Count * 45f);
            for (int i = 0; i < set.Declarations.Count; i++)
            {
                Declaration d = set.Declarations[i];
                RectTransform card = Sliced(list, Slot, 0, i * 45, Right, 42);
                string title = d.Kind == EffectKind.Trait ? TraitName(d.Value) : T("stat_" + d.Value) + " " + d.Amount.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
                Text(card, Fit(title, 7, Right - 114), 6, 2, Right - 114, 13, 7, Gold, TextAnchor.MiddleLeft);
                Text(card, T(d.Kind == EffectKind.Stat || d.Lifetime == Lifetime.Membership ? "while_member" : "permanent"), 6, 18, Right - 12, 12, 6, Muted, TextAnchor.MiddleLeft);
                LabelButton(card, T(d.Enabled ? "on" : "off"), Right - 105, 2, 32, 13, () => { d.Enabled = !d.Enabled; Save(set); }, d.Enabled);
                LabelButton(card, T("edit"), Right - 70, 2, 32, 13, () =>
                { effect = new Declaration { Id = d.Id, Kind = d.Kind, Enabled = d.Enabled, Value = d.Value, Amount = d.Amount, Lifetime = d.Lifetime }; Build(); });
                LabelButton(card, "×", Right - 34, 2, 28, 13, () => { set.Declarations.Remove(d); Save(set); });
            }
        }

        static void BuildDeclaration(RectTransform pane)
        {
            string label = effect.Kind == EffectKind.Trait ? TraitName(effect.Value) : T("stat_" + effect.Value);
            Text(pane, Fit(label, 10, Right - 20), 10, 45, Right - 20, 22, 10, Gold, TextAnchor.MiddleCenter);
            if (effect.Kind == EffectKind.Trait)
            {
                LabelButton(pane, T(effect.Lifetime == Lifetime.Membership ? "while_member" : "permanent"), 5, 85, Right - 10, 25,
                    () => { effect.Lifetime = effect.Lifetime == Lifetime.Membership ? Lifetime.Permanent : Lifetime.Membership; Build(); });
                Paragraph(pane, T("trait_help"), 8, 123, Right - 16, 75);
            }
            else
            {
                Input(pane, effect.Amount.ToString(CultureInfo.InvariantCulture), T("amount"), 8, 85, Right - 16, value =>
                {
                    if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float amount)
                        && !float.IsNaN(amount) && !float.IsInfinity(amount)) effect.Amount = Mathf.Clamp(amount, -9999, 9999);
                });
                Paragraph(pane, T("stat_help"), 8, 123, Right - 16, 75);
            }
            LabelButton(pane, T("cancel"), 0, 250, 170, 22, () => { effect = null; Build(); });
            LabelButton(pane, T("save"), 178, 250, 176, 22, () =>
            {
                if (string.IsNullOrEmpty(effect.Value)) return;
                RuleSet set = Current;
                set.Declarations.RemoveAll(d => d.Id == effect.Id);
                set.Declarations.Add(effect); effect = null; Save(set);
            }, true);
        }

        static void BuildInspection(RectTransform pane)
        {
            RuleSet set = Current;
            int count = WorldRules.MemberCount(selected, out int violations);
            Text(pane, T("members") + ": " + count + "   ·   " + T("violations") + ": " + violations, 5, 67, Right - 10, 20, 8, Gold, TextAnchor.MiddleLeft);
            LabelButton(pane, T(set.Enabled ? "rules_on" : "rules_off"), 5, 94, Right - 10, 22, () => { set.Enabled = !set.Enabled; Save(set); }, set.Enabled);
            Paragraph(pane, T("admission_help"), 7, 129, Right - 14, 75);
            Paragraph(pane, T("provenance_help"), 7, 211, Right - 14, 65);
            Text(pane, T("blocked") + ": " + WorldRules.BlockedAdmissions, 5, 279, Right - 10, 14, 6, Muted, TextAnchor.MiddleLeft);
        }

        static void BuildPicker(RectTransform pane)
        {
            bool metas = pickMeta != null;
            int listY = metas ? 117 : 75;
            if (metas)
                foreach (MetaKind value in Enum.GetValues(typeof(MetaKind)))
                {
                    MetaKind captured = value; int i = (int)value;
                    LabelButton(pane, KindName(value), i % 5 * 71, 39 + i / 5 * 20, 68, 18,
                        () => { pickKind = captured; page = 0; Build(); }, pickKind == value);
                }
            Input(pane, pickQuery, T("search"), 0, metas ? 87 : 45, Right, s => { pickQuery = s; page = 0; Build(); });
            var entries = new List<(string label, string detail, Action click)>();
            if (pickTrait != null)
                foreach (ActorTrait t in AssetManager.traits.list)
                {
                    ActorTrait trait = t;
                    string label = TraitName(t.id);
                    if (!Matches(label + " " + t.id, pickQuery)) continue;
                    entries.Add((label, t.id, () => pickTrait(trait)));
                }
            else if (metas)
                foreach (MetaHandle m in WorldRules.Metas)
                {
                    MetaHandle meta = m;
                    if (m.Kind == pickKind && Matches(m.Name, pickQuery)) entries.Add((m.Name, "#" + m.Id, () => pickMeta(meta)));
                }
            else
                foreach (string stat in WorldRules.AllowedStats)
                {
                    string id = stat;
                    if (Matches(T("stat_" + stat) + " " + stat, pickQuery)) entries.Add((T("stat_" + stat), stat, () => pickStat(id)));
                }
            entries.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.CurrentCultureIgnoreCase));
            const int perPage = 40;
            int pages = Math.Max(1, (entries.Count + perPage - 1) / perPage);
            page = Math.Min(page, pages - 1);
            var visible = entries.Skip(page * perPage).Take(perPage).ToList();
            RectTransform list = Scroll(pane, "picker", 0, listY, Right, 245 - listY, visible.Count * 26);
            for (int i = 0; i < visible.Count; i++)
            {
                var entry = visible[i];
                RectTransform row = LabelButton(list, Fit(entry.label, 7, Right - 16), 0, i * 26, Right, 24, entry.click);
                Text(row, entry.detail, 7, 15, Right - 14, 8, 5, Muted, TextAnchor.LowerRight);
            }
            LabelButton(pane, "‹", 0, 253, 40, 20, () => { page = Math.Max(0, page - 1); Build(); });
            Text(pane, (page + 1) + " / " + pages, 43, 253, 75, 20, 7, Ink, TextAnchor.MiddleCenter);
            LabelButton(pane, "›", 121, 253, 40, 20, () => { page = Math.Min(pages - 1, page + 1); Build(); });
            LabelButton(pane, T("back"), Right - 100, 253, 100, 20, () =>
            { ClearPicker(); if (effect != null && string.IsNullOrEmpty(effect.Value)) effect = null; Build(); });
        }

        static bool Matches(string value, string needle) => string.IsNullOrEmpty(needle) || value.IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;

        static RectTransform LabelButton(Transform parent, string label, float x, float y, float w, float h, Action click, bool active = false)
        {
            RectTransform button = Button(parent, x, y, w, h, "ui/special/special_buttonGray", click);
            if (active) Frame(button, 0);
            Text(button, label, 2, 0, w - 4, h, 7, active ? Gold : Ink, TextAnchor.MiddleCenter);
            return button;
        }

        static void Paragraph(Transform parent, string value, float x, float y, float w, float h)
        {
            var text = Text(parent, value, x, y, w, h, 7, Muted, TextAnchor.UpperLeft).GetComponent<Text>();
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        static void Input(Transform parent, string value, string placeholder, float x, float y, float w, Action<string> commit)
        {
            RectTransform frame = Sliced(parent, Slot, x, y, w, 20);
            Image image = frame.GetComponent<Image>(); image.raycastTarget = true;
            InputField input = frame.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            Text text = Text(frame, "", 5, 1, w - 10, 18, 7, Ink, TextAnchor.MiddleLeft).GetComponent<Text>();
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            input.textComponent = text;
            input.placeholder = Text(frame, placeholder, 5, 1, w - 10, 18, 7, Muted, TextAnchor.MiddleLeft).GetComponent<Text>();
            input.text = value; input.characterLimit = 120;
            // Commit on Enter/focus loss, preserving keyboard focus while typing.
            input.onEndEdit.AddListener(s => { if (s != value) commit(s); });
        }
    }
}
