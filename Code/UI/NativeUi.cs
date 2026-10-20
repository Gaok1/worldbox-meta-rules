using System;
using System.Reflection;
using MetaRules.Core;
using MetaRules.Game;
using UnityEngine;
using UnityEngine.UI;
using static MetaRules.UI.UiKit;
using Object = UnityEngine.Object;

namespace MetaRules.UI
{
    // Spawns the game's own prefabs (trait groups, trait/item buttons, meta banners),
    // so the picker looks and behaves like the native editors. Each has a plain fallback.
    public static class NativeUi
    {
        const string TraitGroupPath = "ui/unit_window_elements/ActorTraitGroup";
        const string TraitButtonPath = "ui/unit_window_elements/ActorTraitEditorButton";
        const string ItemButtonPath = "ui/EquipmentButton";
        public const float CellSize = 28f;
        static GameObject holder;

        public sealed class Group
        {
            public RectTransform Root;
            public Transform Grid;
            public Text Title, Counter;
        }

        // Configure while inactive, so Awake/Start run with our settings in place.
        static T Spawn<T>(string path, Transform parent, Action<T> setup) where T : Component
        {
            T prefab = Resources.Load<T>(path);
            if (prefab == null) return null;
            if (holder == null)
            {
                holder = new GameObject("mr_prefab_holder");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            T made = Object.Instantiate(prefab, holder.transform);
            try { setup?.Invoke(made); }
            catch (Exception error)
            {
                WorldRules.Report(error);
                Object.Destroy(made.gameObject);
                return null;
            }
            made.transform.SetParent(parent, false);
            return made;
        }

        public static Group CreateGroup(Transform parent, string title, Color color)
        {
            ActorTraitGroupElement native = Spawn<ActorTraitGroupElement>(TraitGroupPath, parent, e =>
            {
                for (int i = e.augmentation_buttons_transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(e.augmentation_buttons_transform.GetChild(i).gameObject);
                foreach (LocalizedText text in e.title.GetComponents<LocalizedText>()) Object.DestroyImmediate(text);
                e.title.text = title;
                e.title.color = color;
            });
            if (native != null)
                return new Group { Root = (RectTransform)native.transform, Grid = native.augmentation_buttons_transform, Title = native.title, Counter = native.counter };

            RectTransform root = Box(parent, 0, 0, 210, 20, "group");
            var stack = root.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childControlHeight = true; stack.childControlWidth = true; stack.childForceExpandHeight = false;
            root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Text label = Text(root, title, 0, 0, 210, 16, 8, color, TextAnchor.MiddleCenter).GetComponent<Text>();
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;
            RectTransform grid = Box(root, 0, 0, 210, CellSize, "grid");
            var cells = grid.gameObject.AddComponent<GridLayoutGroup>();
            cells.cellSize = new Vector2(CellSize, CellSize);
            cells.childAlignment = TextAnchor.UpperCenter;
            return new Group { Root = root, Grid = grid, Title = label };
        }

        public static RectTransform TraitButton(Transform parent, ActorTrait trait, bool marked, Action click)
        {
            ActorTraitEditorButton native = Spawn<ActorTraitEditorButton>(TraitButtonPath, parent, e =>
            {
                Undraggable(e.gameObject);
                e.augmentation_button.is_editor_button = true;
                e.augmentation_button.load(trait);
                e.augmentation_button.button.onClick.AddListener(() => click());
                if (e.selected_icon != null)
                {
                    e.selected_icon.gameObject.SetActive(marked);
                    if (marked) e.selected_icon.color = ColorStyleLibrary.m.getSelectorColor();
                }
            });
            return native != null ? (RectTransform)native.transform : null;
        }

        public static RectTransform ItemButton(Transform parent, EquipmentAsset item, bool marked, Action click)
        {
            EquipmentButton native = Spawn<EquipmentButton>(ItemButtonPath, parent, e =>
            {
                Undraggable(e.gameObject);
                e.is_editor_button = true;
                e.load(item);
                // The native click opens the item window of a real item; there is none here.
                TipButton tip = e.GetComponent<TipButton>();
                if (tip != null) tip.clickAction = null;
                e.button.onClick.AddListener(() => click());
            });
            if (native == null) return null;
            if (marked) Frame((RectTransform)native.transform, 0);
            return (RectTransform)native.transform;
        }

        // A wrapper of the requested size holds the native banner, scaled from its 40px design.
        public static RectTransform Banner(Transform parent, MetaHandle meta, float size, Action click)
        {
            RectTransform wrapper = Button(parent, 0, 0, size, size, null, click);
            BannerBase native = null;
            if (Enum.TryParse(meta.Kind.ToString(), out MetaType type) && meta.Target is NanoObject target)
            {
                string path = AssetManager.meta_customization_library.getAsset(type)?.banner_prefab_id;
                if (!string.IsNullOrEmpty(path))
                    native = Spawn<BannerBase>(path, wrapper, e =>
                    {
                        // Clicking must select the meta here, not open its window.
                        foreach (string flag in new[] { "enable_default_click", "_enable_customize_click", "enable_tab_show_click" })
                            Field(e.GetType(), flag)?.SetValue(e, false);
                        e.load(target);
                    });
            }
            if (native == null)
            {
                MetaSymbols.Draw(wrapper, meta, 0, 0, size);
                return wrapper;
            }
            var rt = (RectTransform)native.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = Vector3.one * (size / 40f);
            Button inner = native.GetComponentInChildren<Button>(true);
            if (inner != null) inner.onClick.AddListener(() => click());
            return wrapper;
        }

        // Banner click flags live on the generic base class, some of them private.
        static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(bool)) return field;
            }
            return null;
        }

        // Dragging inside the list must scroll it, not pick up the icon.
        static void Undraggable(GameObject target)
        {
            foreach (DraggableLayoutElement drag in target.GetComponentsInChildren<DraggableLayoutElement>(true))
                Object.DestroyImmediate(drag);
        }
    }
}
