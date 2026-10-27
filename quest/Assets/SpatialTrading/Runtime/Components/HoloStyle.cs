using System;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Hologram palette and sizes for the spread panels. Canvas units are millimetres (world canvas scale 0.001).
    /// Cyan is the linework; amber is reserved for the order panel and filings.
    /// </summary>
    public static class HoloStyle
    {
        public static readonly Color Cyan = new Color32(0x00, 0xE5, 0xFF, 0xFF);
        public static readonly Color Amber = new Color32(0xFF, 0xB3, 0x00, 0xFF);
        public static readonly Color Ink = new Color32(0xE6, 0xFD, 0xFF, 0xFF);
        public static readonly Color Muted = new Color32(0x6F, 0xA3, 0xB0, 0xFF);
        public static readonly Color AskInk = new Color32(0x7F, 0xDC, 0xFF, 0xFF);
        public static readonly Color BrightCyan = new Color32(0xA6, 0xF7, 0xFF, 0xFF);
        /// <summary>Dark translucent fill (top of the gradient); the bottom is a darker shade of it.</summary>
        public static readonly Color PanelFill = new Color(0.016f, 0.07f, 0.095f, 0.82f);
        public static readonly Color OrderFill = new Color(0.085f, 0.058f, 0.012f, 0.82f);

        public const float MinText = 16f;
        public const float MinNumber = 22f;
        public const float LineWidth = 2f;
        public const float SelectedLineWidth = 3f;

        /// <summary>Millimetres per grid cell (canvas units).</summary>
        public const float CellMm = SpreadLayout.CellMeters * 1000f;

        public static Vector2 SizeFor(PanelRect rect) => new Vector2(rect.Width * CellMm, rect.Height * CellMm);

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    /// <summary>Code-built UI pieces in the same top-left, millimetre convention the scene generator uses.</summary>
    internal static class Holo
    {
        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1);
            r.pivot = new Vector2(0, 1);
            r.localScale = Vector3.one;
            return r;
        }

        /// <summary>Top-left placement: x right, y down, in millimetres from the parent's top-left corner.</summary>
        public static void Place(Component c, float x, float y, float w, float h)
        {
            var r = (RectTransform)c.transform;
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        public static void Stretch(Component c)
        {
            var r = (RectTransform)c.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(.5f, .5f);
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        /// <summary>Noto Sans KR line box is 1.448 em; a rect at least this tall never ellipsizes a single line away.</summary>
        public static float LineHeight(float size) => Mathf.Ceil(size * 1.46f) + 2f;

        /// <param name="tight">Overflow instead of ellipsis, for rows whose pitch is below the full line box.</param>
        public static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool tight = false)
        {
            var t = Node(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = Mathf.Max(size, HoloStyle.MinText);
            t.enableAutoSizing = false;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false; // view strings are data, never markup
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = tight ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            t.text = string.Empty;
            return t;
        }

        public static float Width(TMP_Text t, string text) =>
            string.IsNullOrEmpty(text) ? 0f : t.GetPreferredValues(text, 4000f, 400f).x;

        public static SpatialCardGraphic Card(Transform parent, string name, Color fill, Color edge, float edgeWidth,
            float radius, bool raycast = false)
        {
            var g = Node(parent, name).gameObject.AddComponent<SpatialCardGraphic>();
            g.Radius = radius;
            g.raycastTarget = raycast;
            Paint(g, fill, edge, edgeWidth);
            return g;
        }

        /// <summary>Restyles a card; a no-op (no mesh rebuild) when nothing changed.</summary>
        public static void Paint(SpatialCardGraphic g, Color fill, Color edge, float edgeWidth)
        {
            var bottom = new Color(fill.r * .55f, fill.g * .55f, fill.b * .6f, fill.a);
            if (g.color == fill && g.BottomColor == bottom && g.EdgeColor == edge && Mathf.Approximately(g.EdgeWidth, edgeWidth)) return;
            g.BottomColor = bottom;
            g.EdgeColor = edge;
            g.EdgeWidth = edgeWidth;
            g.color = fill;
            g.SetVerticesDirty();
        }

        public static Image Bar(Transform parent, string name, Color color)
        {
            var i = Node(parent, name).gameObject.AddComponent<Image>();
            i.color = color;
            i.raycastTarget = false;
            return i;
        }

        public static Button Button(Transform parent, string name)
        {
            var card = Card(parent, name, Color.clear, HoloStyle.Cyan, HoloStyle.LineWidth, 6f, true);
            var b = card.gameObject.AddComponent<Button>();
            b.targetGraphic = card;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            var c = b.colors;
            c.normalColor = Color.white;
            c.selectedColor = Color.white;
            c.highlightedColor = new Color(.72f, 1f, 1f);
            c.pressedColor = new Color(.45f, .85f, 1f);
            c.disabledColor = new Color(1f, 1f, 1f, .4f);
            b.colors = c;
            return b;
        }

        /// <summary>One activation per frame even if the SDK reports two completed pointers in the same frame.</summary>
        public static void OnClick(Button b, Action action)
        {
            var last = -1;
            b.onClick.AddListener(() =>
            {
                if (last == Time.frameCount) return;
                last = Time.frameCount;
                action();
            });
        }

        /// <summary>A 16 x 20 mm padlock (outlined shackle over a filled body); NotoSansKR has no U+1F512 glyph.</summary>
        public static RectTransform LockIcon(Transform parent, string name, Color color)
        {
            var root = Node(parent, name);
            root.sizeDelta = new Vector2(16f, 20f);
            var shackle = Card(root, "Shackle", Color.clear, color, 2f, 5f);
            Place(shackle, 3f, 0f, 10f, 12f);
            var body = Card(root, "Body", color, color, 0f, 2f);
            Place(body, 0f, 8f, 16f, 12f);
            Tint(root, color);
            return root;
        }

        public static void Tint(RectTransform lockIcon, Color color)
        {
            foreach (var g in lockIcon.GetComponentsInChildren<SpatialCardGraphic>(true))
            {
                g.EdgeColor = color;
                g.BottomColor = g.name == "Body" ? color : Color.clear;
                g.color = g.name == "Body" ? color : Color.clear;
                g.SetVerticesDirty();
            }
        }

        public static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        public static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }

    /// <summary>
    /// Shared frame for the flat, front-facing spread panels: translucent fill, accent edge, HUD corner brackets and a
    /// header rule. Children are built in code on first <c>Configure</c> or <c>Render</c>; the panel keeps scale 1 and
    /// sizes itself to the grid rect (cells × 50 mm).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class HoloPanel : MonoBehaviour
    {
        protected const float Pad = 14f;
        protected const float Top = 10f;
        protected const float HeaderH = 31f;
        protected const float TitleSize = 20f;

        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private SpatialCardGraphic _frame;
        [SerializeField] private Image[] _corners;
        [SerializeField] private Image _rule;
        private bool _wired;

        public TMP_FontAsset Font => _font;
        protected RectTransform Root => (RectTransform)transform;
        protected virtual Color Accent => HoloStyle.Cyan;
        protected virtual Color Fill => HoloStyle.PanelFill;

        /// <summary>Injects the Korean TMP font (built by the editor tooling) and builds the panel's children.</summary>
        public void Configure(TMP_FontAsset font)
        {
            _font = font;
            EnsureBuilt();
            if (font == null) return;
            foreach (var t in GetComponentsInChildren<TMP_Text>(true)) t.font = font;
            OnFont(font);
        }

        protected virtual void OnFont(TMP_FontAsset font) { }
        protected abstract void Build();
        protected virtual void Wire() { }

        protected void EnsureBuilt()
        {
            if (_frame == null) { BuildFrame(); Build(); }
            if (!_wired) { _wired = true; Wire(); }
        }

        /// <summary>Applies the grid size (scale stays 1) and returns it in millimetres.</summary>
        protected Vector2 Size(PanelRect rect)
        {
            var root = Root;
            if (root.localScale != Vector3.one) root.localScale = Vector3.one;
            var size = HoloStyle.SizeFor(rect);
            if (root.sizeDelta != size) root.sizeDelta = size;
            return size;
        }

        /// <summary>Header rule under the title row, at <paramref name="y"/> mm from the top.</summary>
        protected void Rule(float width, float y) => Holo.Place(_rule, Pad, y, width - 2 * Pad, 1.5f);

        protected void SetFrame(bool bright)
        {
            var edge = bright ? (Accent == HoloStyle.Cyan ? HoloStyle.BrightCyan : Accent) : HoloStyle.Alpha(Accent, .55f);
            Holo.Paint(_frame, Fill, edge, bright ? HoloStyle.SelectedLineWidth : HoloStyle.LineWidth);
            foreach (var c in _corners) c.color = bright ? edge : HoloStyle.Alpha(Accent, .9f);
        }

        private void BuildFrame()
        {
            _frame = Holo.Card(transform, "Frame", Fill, HoloStyle.Alpha(Accent, .55f), HoloStyle.LineWidth, 8f, true);
            Holo.Stretch(_frame);
            _frame.transform.SetAsFirstSibling();
            _corners = new Image[8];
            const float arm = 18f, thick = 3f, out_ = 3f;
            for (var i = 0; i < 4; i++)
            {
                var corner = new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 0 : 1); // TL, TR, BR, BL
                var sx = corner.x == 0 ? 1f : -1f;
                var sy = corner.y == 1 ? -1f : 1f;
                for (var k = 0; k < 2; k++)
                {
                    var bar = Holo.Bar(_frame.transform, "Corner", HoloStyle.Alpha(Accent, .9f));
                    var r = bar.rectTransform;
                    r.anchorMin = r.anchorMax = corner;
                    r.pivot = corner;
                    r.anchoredPosition = new Vector2(-sx * out_, -sy * out_);
                    r.sizeDelta = k == 0 ? new Vector2(arm, thick) : new Vector2(thick, arm);
                    _corners[i * 2 + k] = bar;
                }
            }
            _rule = Holo.Bar(transform, "Rule", HoloStyle.Alpha(Accent, .28f));
            SetFrame(false);
        }
    }
}
