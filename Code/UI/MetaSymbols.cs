using System;
using System.Collections.Generic;
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
        public static Sprite KindSprite(MetaKind kind)
        {
            if (kind == MetaKind.Equipment) return EquipmentSprite();
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

        static readonly Dictionary<string, string> statIcons = new Dictionary<string, string>
        {
            { "health", "iconHealth" }, { "damage", "iconDamage" }, { "armor", "iconArmor" },
            { "speed", "iconSpeed" }, { "attack_speed", "iconAttackSpeed" }, { "intelligence", "iconIntelligence" },
            { "warfare", "iconWarfare" }, { "diplomacy", "iconDiplomacy" }, { "stewardship", "iconStewardship" }
        };

        public static Sprite StatSprite(string id) =>
            SpriteTextureLoader.getSprite("ui/Icons/" + (statIcons.TryGetValue(id, out string name) ? name : "iconWar"))
            ?? SpriteTextureLoader.getSprite("ui/Icons/iconWar");

        public static Sprite EquipmentSprite() => SpriteTextureLoader.getSprite("ui/Icons/iconEquipmentEditor")
            ?? SpriteTextureLoader.getSprite("ui/Icons/iconArmor");

        public static Sprite ItemSprite(string id)
        {
            Sprite sprite = null;
            try { sprite = AssetManager.items.get(id)?.getSprite(); } catch (Exception) { /* Missing icon path. */ }
            return sprite ?? EquipmentSprite();
        }

        // Mirrors each native *Banner.setupBanner, so every meta shows its own emblem.
        public static void Draw(Transform parent, MetaHandle meta, float x, float y, float size)
        {
            if (meta == null) return;
            if (meta.Target is Item item)
            {
                Picture(parent, ItemSprite(item.getAsset().id), x, y, size, size);
                return;
            }
            Sprite background = null, icon = null, overlay = null, frame = null;
            ColorAsset color = null;
            bool tintIcon = true;
            try
            {
                switch (meta.Target)
                {
                    case Kingdom k: background = k.getElementBackground(); icon = k.getElementIcon(); color = k.getColor(); break;
                    case City c when c.kingdom != null:
                        background = c.kingdom.getElementBackground(); icon = c.kingdom.getElementIcon(); color = c.kingdom.getColor(); break;
                    case Army a when a.getKingdom() != null:
                        Kingdom owner = a.getKingdom();
                        background = owner.getElementBackground(); icon = owner.getElementIcon(); color = owner.getColor(); break;
                    case Clan c: background = c.getBackgroundSprite(); icon = c.getIconSprite(); color = c.getColor(); break;
                    case Culture c: background = c.getDecorSprite(); icon = c.getElementSprite(); color = c.getColor(); break;
                    case Religion r: background = r.getBackgroundSprite(); icon = r.getIconSprite(); color = r.getColor(); break;
                    case Language l: background = l.getBackgroundSprite(); icon = l.getIconSprite(); color = l.getColor(); break;
                    case Family f: background = f.getSpriteBackground(); icon = f.getSpriteIcon(); frame = f.getSpriteFrame(); color = f.getColor(); tintIcon = false; break;
                    case Subspecies s:
                        background = s.getSpriteBackground(); icon = s.getSpriteIcon(); overlay = s.getUnitSpriteForBanner();
                        color = s.getColor(); tintIcon = false; break;
                }
            }
            catch (Exception) { /* A meta in mid-construction (e.g. army without captain) has no color yet. */ }

            if (background != null)
            {
                Image layer = Picture(parent, background, x, y, size, size).GetComponent<Image>();
                if (color != null) layer.color = color.getColorMainSecond();
            }
            if (icon != null)
            {
                Image layer = Picture(parent, icon, x, y, size, size).GetComponent<Image>();
                if (color != null && tintIcon) layer.color = color.getColorBanner();
            }
            if (frame != null) Picture(parent, frame, x, y, size, size);
            if (overlay != null) Picture(parent, overlay, x + size * 0.2f, y + size * 0.2f, size * 0.6f, size * 0.6f);
            if (background == null && icon == null)
            {
                Image fallback = Picture(parent, KindSprite(meta.Kind), x, y, size, size).GetComponent<Image>();
                if (color != null) fallback.color = Color.Lerp(color.getColorMain(), Color.white, 0.3f);
            }
            // A small type badge distinguishes, for example, a clan banner from a kingdom banner.
            if (size >= 30)
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
