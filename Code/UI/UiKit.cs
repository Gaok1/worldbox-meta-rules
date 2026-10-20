// Adapted from Diplomacy Overhaul, Copyright (c) 2026 Lapis0x0. MIT; see THIRD_PARTY_NOTICES.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace MetaRules.UI
{
    // 用原版贴图拼界面的小工具：坐标都从父节点左上角算，y 向下
    public static class UiKit
    {
        public static readonly Color Ink = Hex("#f4f1e8");
        public static readonly Color Muted = Hex("#cfc8b4");
        public static readonly Color Gold = Hex("#ffb52e");
        public static readonly Color HeaderGold = Hex("#ffa827");
        public static readonly Color Line = Hex("#5d6253");
        public static readonly Color Edge = Hex("#1a1a18");
        public const string Slot = "ui/special/windowInnerSlicedRound";

        public static Color Hex(string pHex)
        {
            ColorUtility.TryParseHtmlString(pHex, out Color c);
            return c;
        }

        public static string Tag(Color pColor) => "#" + ColorUtility.ToHtmlStringRGB(pColor);

        public static RectTransform Box(Transform pParent, float pX, float pY, float pW, float pH, string pName = "box")
        {
            var go = new GameObject(pName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(pParent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(pX, -pY);
            rt.sizeDelta = new Vector2(pW, pH);
            return rt;
        }

        public static RectTransform Sliced(Transform pParent, string pSprite, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "slot");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteTextureLoader.getSprite(pSprite);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rt;
        }

        public static RectTransform Rect(Transform pParent, float pX, float pY, float pW, float pH, Color pColor)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "rect");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = pColor;
            img.raycastTarget = false;
            return rt;
        }

        // 带深色描边的色块：议席、氏族的派系色
        public static RectTransform Swatch(Transform pParent, float pX, float pY, float pSize, Color pColor, float pAlpha)
        {
            RectTransform rt = Rect(pParent, pX, pY, pSize, pSize, new Color(pColor.r, pColor.g, pColor.b, pAlpha));
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Edge.r, Edge.g, Edge.b, pAlpha);
            outline.effectDistance = new Vector2(0.5f, -0.5f);
            return rt;
        }

        public static RectTransform Icon(Transform pParent, string pSprite, float pX, float pY, float pSize) =>
            Picture(pParent, SpriteTextureLoader.getSprite(pSprite), pX, pY, pSize, pSize);

        public static RectTransform Picture(Transform pParent, Sprite pSprite, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "icon");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = pSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rt;
        }

        // 选中的黄框
        public static RectTransform Frame(RectTransform pTarget, float pInset)
        {
            RectTransform rt = Sliced(pTarget, "ui/special/buttonSelected", 0f, 0f, 0f, 0f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pInset, pInset);
            rt.offsetMax = new Vector2(-pInset, -pInset);
            rt.GetComponent<Image>().fillCenter = false;
            return rt;
        }

        public static RectTransform Button(Transform pParent, float pX, float pY, float pW, float pH, string pSprite, Action pClick)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "button");
            Image img = rt.gameObject.AddComponent<Image>();
            if (pSprite != null)
            {
                img.sprite = SpriteTextureLoader.getSprite(pSprite);
                img.type = Image.Type.Sliced;
            }
            else img.color = new Color(0f, 0f, 0f, 0f);
            var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => pClick());
            return rt;
        }

        public static RectTransform Text(Transform pParent, string pText, float pX, float pY, float pW, float pH, int pSize, Color pColor, TextAnchor pAlign)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "text");
            UnityEngine.UI.Text t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = LocalizedTextManager.current_font;
            t.fontSize = pSize;
            t.color = pColor;
            t.alignment = pAlign;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = pText;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Hex("#151515");
            shadow.effectDistance = new Vector2(0.6f, -0.6f);
            return rt;
        }

        public static float Measure(string pText, int pSize)
        {
            Font font = LocalizedTextManager.current_font;
            font.RequestCharactersInTexture(pText, pSize);
            float w = 0f;
            foreach (char c in pText)
            {
                if (font.GetCharacterInfo(c, out CharacterInfo info, pSize)) w += info.advance;
                else w += pSize;
            }
            return w;
        }

        // 放不下时截短，末尾加省略号
        public static string Fit(string pText, int pSize, float pWidth)
        {
            if (Measure(pText, pSize) <= pWidth) return pText;
            for (int n = pText.Length - 1; n > 0; n--)
            {
                string s = pText.Substring(0, n) + "…";
                if (Measure(s, pSize) <= pWidth) return s;
            }
            return "…";
        }

        // 分区标题条：原版的标签页底图，两端各一个图标
        public static void Header(Transform pParent, float pX, float pY, float pW, string pText, string pIcon)
        {
            RectTransform bar = Sliced(pParent, "ui/special/background_tab_content", pX, pY, pW, 20f);
            Icon(bar, pIcon, 6f, 3f, 14f);
            Icon(bar, pIcon, pW - 20f, 3f, 14f);
            Text(bar, pText, 0f, 0f, pW, 18f, 9, HeaderGold, TextAnchor.MiddleCenter);
        }

        // 能滚动的列表，返回内容节点
        public static RectTransform Scroll(Transform pParent, string pName, float pX, float pY, float pW, float pH, float pContentH)
        {
            RectTransform view = Box(pParent, pX, pY, pW, pH, pName);
            view.gameObject.AddComponent<RectMask2D>();
            Image hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform content = Box(view, 0f, 0f, pW, Mathf.Max(pH, pContentH), "content");
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = pContentH > pH;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;
            return content;
        }

    }
}
