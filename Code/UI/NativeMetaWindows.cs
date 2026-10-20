// Native-tab integration follows Diplomacy Overhaul's MIT-licensed approach.
// Copyright (c) 2026 Lapis0x0; see THIRD_PARTY_NOTICES.
using System;
using HarmonyLib;
using MetaRules.Core;
using MetaRules.Game;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MetaRules.UI
{
    public static class NativeMetaWindows
    {
        const string ButtonId = "mr_meta_rules_button";

        public static void RefreshOpenWindow()
        {
            ScrollWindow current = ScrollWindow._current_window;
            if (current != null && current.gameObject.activeInHierarchy) Attach(current.screen_id);
        }

        public static MetaHandle Selection(string id)
        {
            switch (id)
            {
                case "army": return WorldRules.Handle(MetaKind.Army, SelectedMetas.selected_army, SelectedMetas.selected_army?.data);
                case "clan": return WorldRules.Handle(MetaKind.Clan, SelectedMetas.selected_clan, SelectedMetas.selected_clan?.data);
                case "culture": return WorldRules.Handle(MetaKind.Culture, SelectedMetas.selected_culture, SelectedMetas.selected_culture?.data);
                case "religion": return WorldRules.Handle(MetaKind.Religion, SelectedMetas.selected_religion, SelectedMetas.selected_religion?.data);
                case "city": return WorldRules.Handle(MetaKind.City, SelectedMetas.selected_city, SelectedMetas.selected_city?.data);
                case "kingdom": return WorldRules.Handle(MetaKind.Kingdom, SelectedMetas.selected_kingdom, SelectedMetas.selected_kingdom?.data);
                case "language": return WorldRules.Handle(MetaKind.Language, SelectedMetas.selected_language, SelectedMetas.selected_language?.data);
                case "family": return WorldRules.Handle(MetaKind.Family, SelectedMetas.selected_family, SelectedMetas.selected_family?.data);
                case "subspecies": return WorldRules.Handle(MetaKind.Subspecies, SelectedMetas.selected_subspecies, SelectedMetas.selected_subspecies?.data);
                default: return null;
            }
        }

        public static void Attach(string id)
        {
            if (Selection(id) == null || !ScrollWindow._all_windows.TryGetValue(id, out ScrollWindow window) || window == null) return;
            Transform bg = window.transform.Find("Background");
            if (bg == null) return;
            Transform tabs = bg.Find("Tabs");
            if (tabs != null && tabs.Find(ButtonId) != null || bg.Find(ButtonId) != null) return;
            Transform template = null;
            if (tabs != null)
                foreach (Transform child in tabs)
                    if (child.GetComponent<WindowMetaTab>() != null && child.GetComponent<Button>() != null)
                    { template = child; break; }
            GameObject go;
            if (template != null)
            {
                var holder = new GameObject("mr_inactive_holder");
                holder.SetActive(false);
                go = Object.Instantiate(template.gameObject, holder.transform);
                go.name = ButtonId;
                foreach (Component c in go.GetComponents<Component>())
                    if (c is WindowMetaTab || c is DragOrderElement || c is ScrollableButton || c is UnityEngine.EventSystems.EventTrigger)
                        Object.DestroyImmediate(c);
                go.transform.SetParent(tabs, false);
                go.transform.SetSiblingIndex(1);
                Object.Destroy(holder);
                go.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/tab_button_vertical");
                Image icon = go.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null) icon.sprite = SpriteTextureLoader.getSprite("ui/Icons/iconOptions");
                go.SetActive(true);
            }
            else
            {
                // Fallback for windows without native meta tabs (or altered by another mod).
                var button = UiKit.Button(bg, -25, 28, 22, 22, "ui/tab_button_vertical", () => { });
                button.name = ButtonId;
                UiKit.Icon(button, "ui/Icons/iconOptions", 4, 4, 14);
                go = button.gameObject;
            }
            TipButton tip = go.GetComponent<TipButton>() ?? go.AddComponent<TipButton>();
            tip.textOnClick = "mr_open";
            tip.textOnClickDescription = "mr_open_description";
            tip.text_description_2 = "";
            Button action = go.GetComponent<Button>();
            action.onClick = new Button.ButtonClickedEvent();
            action.onClick.AddListener(() =>
            {
                // Resolve on click, not when the tab was first created for another object.
                MetaHandle meta = Selection(id);
                if (meta != null) RulesWindow.OpenFor(meta);
            });
            if (tabs != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tabs);
        }
    }

    [HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.showWindow), new[] { typeof(string), typeof(bool), typeof(bool) })]
    static class ShowNativeMetaRules
    {
        static void Postfix(string pWindowID)
        {
            try { NativeMetaWindows.Attach(pWindowID); }
            catch (Exception error) { WorldRules.Report(error); }
        }
    }
}
