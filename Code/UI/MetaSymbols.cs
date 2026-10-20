using System;
using System.Collections.Generic;
using System.Reflection;
using MetaRules.Core;
using MetaRules.Game;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MetaRules.UI.UiKit;

namespace MetaRules.UI
{
    public static class MetaSymbols
    {
        sealed class BannerAccess
        {
            public MethodInfo Background;
            public MethodInfo Icon;
        }
        static readonly Dictionary<Type, BannerAccess> accessors = new Dictionary<Type, BannerAccess>();

        public static Sprite KindSprite(MetaKind kind)
        {
            if (Enum.TryParse(kind.ToString(), out MetaType type))
            {
                MetaTypeAsset asset = AssetManager.meta_type_library.getAsset(type);
                if (asset != null && !string.IsNullOrEmpty(asset.icon_single_path))
                {
                    Sprite sprite = SpriteTextureLoader.getSprite(asset.icon_single_path);
                    if (sprite != null) return sprite;
                }
            }
            return SpriteTextureLoader.getSprite("ui/Icons/iconClan");
        }

        public static Sprite TraitSprite(string id) => AssetManager.traits.get(id)?.getSprite()
            ?? SpriteTextureLoader.getSprite("ui/Icons/iconTraits");

        public static Sprite StatSprite(string id)
        {
            var names = new Dictionary<string, string>
            {
                { "health", "iconHealth" }, { "damage", "iconDamage" }, { "armor", "iconArmor" },
                { "speed", "iconSpeed" }, { "attack_speed", "iconAttackSpeed" }, { "intelligence", "iconIntelligence" },
                { "warfare", "iconWarfare" }, { "diplomacy", "iconDiplomacy" }, { "stewardship", "iconStewardship" }
            };
            return SpriteTextureLoader.getSprite("ui/Icons/" + (names.TryGetValue(id, out string name) ? name : "iconWar"))
                ?? SpriteTextureLoader.getSprite("ui/Icons/iconWar");
        }

        public static void Draw(Transform parent, MetaHandle meta, float x, float y, float size)
        {
            if (meta == null) return;
            var target = meta.Target as IMetaObject;
            Sprite background = null, icon = null;
            if (target != null)
            {
                Type type = target.GetType();
                if (!accessors.TryGetValue(type, out BannerAccess access))
                {
                    access = new BannerAccess { Background = type.GetMethod("getBackgroundSprite", Type.EmptyTypes), Icon = type.GetMethod("getIconSprite", Type.EmptyTypes) };
                    accessors[type] = access;
                }
                if (access.Background != null) background = access.Background.Invoke(target, null) as Sprite;
                if (access.Icon != null) icon = access.Icon.Invoke(target, null) as Sprite;
            }
            ColorAsset color = null;
            try { color = target?.getColor(); } catch (Exception) { /* An army without a captain can have no color yet. */ }
            if (background != null)
            {
                Image layer = Picture(parent, background, x, y, size, size).GetComponent<Image>();
                if (color != null) layer.color = color.getColorMainSecond();
            }
            Sprite symbol = icon ?? KindSprite(meta.Kind);
            Image foreground = Picture(parent, symbol, x, y, size, size).GetComponent<Image>();
            if (icon != null && color != null) foreground.color = color.getColorBanner();
            else if (color != null) foreground.color = Color.Lerp(color.getColorMain(), Color.white, 0.3f);
            // A small type badge distinguishes, for example, a clan banner from a kingdom banner.
            if (icon != null && size >= 30)
            {
                Rect(parent, x + size - 10, y + size - 10, 11, 11, Edge);
                Picture(parent, KindSprite(meta.Kind), x + size - 9, y + size - 9, 9, 9);
            }
        }

        public static void Tip(RectTransform target, Func<string> title, Func<string> body = null)
        {
            var hover = target.gameObject.AddComponent<SymbolTooltip>();
            hover.Title = title;
            hover.Body = body;
        }
    }

    // Only handle hover: EventTrigger would also consume scroll/drag events and
    // prevent the parent ScrollRect from scrolling while the pointer is over a token.
    public sealed class SymbolTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Title, Body;
        bool visible;
        public void OnPointerEnter(PointerEventData data)
        {
            visible = true;
            NeoModLoader.General.LM.AddToCurrentLocale("mr_hover_title", Title());
            NeoModLoader.General.LM.AddToCurrentLocale("mr_hover_body", Body?.Invoke() ?? "");
            Tooltip.show(gameObject, "tip", new TooltipData { tip_name = "mr_hover_title", tip_description = "mr_hover_body" });
        }
        public void OnPointerExit(PointerEventData data) { visible = false; Tooltip.hideTooltip(); }
        void OnDisable() { if (visible) { visible = false; Tooltip.hideTooltip(); } }
    }
}
