using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>Colours and small helpers for building UI in code. Every menu uses these, so they all look alike.</summary>
    public static class MenuUi
    {
        public static readonly Color Paper = new(0.93f, 0.91f, 0.84f, 1f);   // warm photo-paper white
        public static readonly Color Dim = new(0.93f, 0.91f, 0.84f, 0.42f);
        public static readonly Color Faint = new(0.93f, 0.91f, 0.84f, 0.1f);
        public static readonly Color Accent = new(1f, 0.5f, 0.2f, 1f);       // the orange date-stamp colour
        public static readonly Color Rec = new(0.9f, 0.14f, 0.12f, 1f);
        public static readonly Color Ok = new(0.55f, 0.92f, 0.45f, 1f);      // focus locked

        /// <summary>The typewriter font (Special Elite). See KadrFont.</summary>
        public static Font Font => KadrFont.Body;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors the rect to one point of its parent and gives it a size and position from there.</summary>
        public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Fills the parent, with optional margins (left, bottom, right, top).</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static RawImage Picture(string name, Transform parent, Texture texture, Color color)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>mono: Share Tech Mono (numbers, values, small labels) instead of the typewriter font.</summary>
        public static Text Label(string name, Transform parent, string text, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, bool mono = false)
        {
            var rect = Rect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            KadrFont.Fit(label, mono ? KadrFont.Mono : KadrFont.Body, text);
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            // Our fonts have one weight only; bold would be a blurry fake.
            label.fontStyle = KadrFont.IsCustom(label.font) ? FontStyle.Normal : style;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>A hollow rectangle made of four bars. Returns its rect (size it yourself).</summary>
        public static RectTransform Frame(Transform parent, string name, Color color, float thickness)
        {
            var root = Rect(name, parent);

            var top = Box("Top", root, color).rectTransform;
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, thickness);
            top.anchoredPosition = Vector2.zero;

            var bottom = Box("Bottom", root, color).rectTransform;
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(0f, thickness);
            bottom.anchoredPosition = Vector2.zero;

            var left = Box("Left", root, color).rectTransform;
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.sizeDelta = new Vector2(thickness, 0f);
            left.anchoredPosition = Vector2.zero;

            var right = Box("Right", root, color).rectTransform;
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 0.5f);
            right.sizeDelta = new Vector2(thickness, 0f);
            right.anchoredPosition = Vector2.zero;

            return root;
        }

        /// <summary>A screen-space overlay canvas that scales with the screen (1920 x 1080 reference).</summary>
        public static Canvas MakeCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }
    }
}
