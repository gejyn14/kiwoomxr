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
        public static readonly Color Muted = From(HoloPalette.Muted);
        public static readonly Color AskInk = new Color32(0x7F, 0xDC, 0xFF, 0xFF);
        public static readonly Color BrightCyan = new Color32(0xA6, 0xF7, 0xFF, 0xFF);
        /// <summary>Dark translucent fill (top of the gradient); the bottom is a darker shade of it.</summary>
        public static readonly Color PanelFill = From(HoloPalette.PanelFill);
        public static readonly Color OrderFill = new Color(0.085f, 0.058f, 0.012f, 0.82f);

        public const float MinText = 16f;
        public const float MinNumber = 22f;
        public const float LineWidth = 2f;
        public const float SelectedLineWidth = 3f;
        /// <summary>Hover and press glow inside a button (<see cref="ButtonFeedback"/>).</summary>
        public const float HoverGlow = .18f;
        public const float PressGlow = .32f;
        public const float PressScale = .97f;
        /// <summary>Half the resize handle's 55 mm grab square, which is centred on the panel's bottom-right corner: no
        /// button may reach into this many millimetres of the corner, in both directions.</summary>
        public const float HandleClearMm = 27.5f;
        /// <summary>The ray-grab strip on a panel's top edge reaches this far down into the panel (it starts 8 mm above
        /// the edge); no button may overlap its footprint.</summary>
        public const float GrabStripDepthMm = 20f;
        public const float GrabStripOverhangMm = 8f;

        /// <summary>Width of the centred ray-grab strip for a panel <paramref name="panelWidthMm"/> wide.</summary>
        public static float GrabStripWidthMm(float panelWidthMm) => Mathf.Min(panelWidthMm * .3f, 140f);

        /// <summary>Millimetres per grid cell (canvas units).</summary>
        public const float CellMm = SpreadLayout.CellMeters * 1000f;

        public static Vector2 SizeFor(PanelRect rect) => new Vector2(rect.Width * CellMm, rect.Height * CellMm);

        public static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private static Color From(Rgba c) => new Color(c.R, c.G, c.B, c.A);

        private static AudioClip _tick;

        /// <summary>The press confirmation: 30 ms of a sine falling from 3 kHz to 2 kHz with a 1 ms attack and a fast
        /// exponential decay. Built once in code; no asset file.</summary>
        public static AudioClip Tick
        {
            get
            {
                if (_tick != null) return _tick;
                const int rate = 44100;
                const float seconds = .03f;
                var n = Mathf.RoundToInt(rate * seconds);
                var data = new float[n];
                var phase = 0.0;
                for (var i = 0; i < n; i++)
                {
                    var t = (float)i / rate;
                    var k = (float)i / n;
                    phase += 2.0 * Math.PI * Mathf.Lerp(3000f, 2000f, k) / rate;
                    var envelope = Mathf.Min(1f, t / .001f) * Mathf.Exp(-t / .006f);
                    data[i] = .5f * envelope * (float)Math.Sin(phase);
                }
                _tick = AudioClip.Create("Tick", n, 1, rate, false);
                _tick.SetData(data, 0);
                return _tick;
            }
        }
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

        /// <summary>Top-left placement: x right, y down, in millimetres from the parent's top-left corner. Honours the
        /// rect's pivot (buttons pivot on their centre so the press scale shrinks them in place).</summary>
        public static void Place(Component c, float x, float y, float w, float h)
        {
            var r = (RectTransform)c.transform;
            w = Mathf.Max(0f, w); h = Mathf.Max(0f, h);
            r.anchoredPosition = new Vector2(x + w * r.pivot.x, -y - h * (1f - r.pivot.y));
            r.sizeDelta = new Vector2(w, h);
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

        /// <summary>A card button with hover/press feedback (<see cref="ButtonFeedback"/>) glowing in
        /// <paramref name="tint"/> (cyan when omitted). Pivot is the centre; place it with <see cref="Place"/>.</summary>
        public static Button Button(Transform parent, string name, Color? tint = null)
        {
            var card = Card(parent, name, Color.clear, HoloStyle.Cyan, HoloStyle.LineWidth, 6f, true);
            card.rectTransform.pivot = new Vector2(.5f, .5f);
            var glow = Card(card.transform, "Glow", Color.clear, Color.clear, HoloStyle.LineWidth, 6f);
            Stretch(glow);
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
            card.gameObject.AddComponent<ButtonFeedback>().Configure(glow, tint ?? HoloStyle.Cyan);
            return b;
        }

        /// <summary>One activation per frame even if the SDK reports two completed pointers in the same frame; every
        /// activation plays <see cref="HoloStyle.Tick"/> at the button (hands have no haptics).</summary>
        public static void OnClick(Button b, Action action)
        {
            var last = -1;
            b.onClick.AddListener(() =>
            {
                if (last == Time.frameCount) return;
                last = Time.frameCount;
                if (Application.isPlaying) AudioSource.PlayClipAtPoint(HoloStyle.Tick, b.transform.position, .5f);
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
    /// header rule, and the 55 mm close button (×) inside the top-right corner. Children are built in code on first
    /// <c>Configure</c> or <c>Render</c>; the panel keeps scale 1 and sizes itself to the grid rect (cells × 50 mm).
    /// Header content ends <see cref="CloseReserveMm"/> short of the right edge, and anything else that reaches into the
    /// right-hand column starts at <see cref="BodyTop"/>, under the close button.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class HoloPanel : MonoBehaviour
    {
        protected const float Pad = 14f;
        protected const float Top = 10f;
        protected const float HeaderH = 31f;
        protected const float TitleSize = 20f;
        public const float CloseSize = 55f;
        /// <summary>Close button inset from the top and right edges.</summary>
        public const float CloseInset = 3f;
        /// <summary>The header's right end kept free for the close button (55 mm + 3 mm inset + 4 mm gap).</summary>
        public const float CloseReserveMm = 62f;
        /// <summary>Bottom edge of the close button, from the panel top.</summary>
        protected const float CloseBottom = CloseInset + CloseSize;
        /// <summary>First body row in panels whose rows span the full width (just under the close button).</summary>
        protected const float BodyTop = CloseBottom + 2f;
        private const float CloseGlyphSize = 34f;

        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private SpatialCardGraphic _frame;
        [SerializeField] private Image[] _corners;
        [SerializeField] private Image _rule;
        [SerializeField] private Button _close;
        private bool _wired;

        /// <summary>The close button (×) was pressed; the workspace dismisses the panel the same way a flick does.</summary>
        public event Action CloseRequested;

        public TMP_FontAsset Font => _font;
        /// <summary>The close button (built with the panel; null before the first Configure or Render).</summary>
        public Button CloseButton => _close;
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
            if (_frame == null) { BuildFrame(); Build(); BuildClose(); }
            if (!_wired)
            {
                _wired = true;
                Holo.OnClick(_close, () => CloseRequested?.Invoke());
                Wire();
            }
        }

        /// <summary>True when <paramref name="world"/> (a point on or just behind the panel) is on the close button: the
        /// editor mouse fallback, which reaches panels through their body collider.</summary>
        public bool IsCloseAt(Vector3 world)
        {
            if (_close == null || !_close.gameObject.activeInHierarchy) return false;
            var r = (RectTransform)_close.transform;
            return r.rect.Contains((Vector2)r.InverseTransformPoint(world));
        }

        /// <summary>Anchored to the top-right corner, so it follows every resize without a layout pass.</summary>
        private void BuildClose()
        {
            _close = Holo.Button(transform, "Close", Accent);
            var r = (RectTransform)_close.transform;
            r.anchorMin = r.anchorMax = Vector2.one;
            r.sizeDelta = new Vector2(CloseSize, CloseSize);
            r.anchoredPosition = new Vector2(-CloseInset - CloseSize / 2f, -CloseInset - CloseSize / 2f);
            Holo.Paint((SpatialCardGraphic)_close.targetGraphic, HoloStyle.Alpha(Accent, .04f), HoloStyle.Alpha(Accent, .5f), 1.5f);
            var glyph = Holo.Text(r, "CloseGlyph", _font, CloseGlyphSize, Accent == HoloStyle.Cyan ? HoloStyle.Ink : Accent,
                TextAlignmentOptions.Center, true);
            Holo.Stretch(glyph);
            glyph.text = "\u00D7"; // ×
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

        /// <summary>Header rule under the title row, at <paramref name="y"/> mm from the top; it stops short of the close
        /// button when it would pass under it.</summary>
        protected void Rule(float width, float y) =>
            Holo.Place(_rule, Pad, y, (y < CloseBottom ? width - CloseReserveMm : width - Pad) - Pad, 1.5f);

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
