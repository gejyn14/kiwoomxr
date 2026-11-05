using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpatialTrading.Components
{
    /// <summary>
    /// Visible hover and press states for a <see cref="Holo.Button"/> (hands have no haptics). The "Glow" card inside the
    /// button lights in the tint at 0.18 on hover and 0.32 while pressed, with a bright edge over the button's own edge,
    /// and the button shrinks to 0.97 while pressed. A non-interactable button does not react. The glow is a separate
    /// graphic, so the owner can keep restyling the button card on render.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private SpatialCardGraphic _glow;
        [SerializeField] private Color _tint = HoloStyle.Cyan;
        [SerializeField] private Selectable _button;

        private readonly HashSet<int> _hovering = new HashSet<int>();
        private readonly HashSet<int> _pressing = new HashSet<int>();
        private float _shown = -1f;

        public SpatialCardGraphic Glow => _glow;

        public void Configure(SpatialCardGraphic glow, Color tint)
        {
            _glow = glow;
            _tint = tint;
            _button = GetComponent<Selectable>();
            Show(0f);
        }

        private bool Live => _button == null || _button.IsInteractable();

        public void OnPointerEnter(PointerEventData e) { if (Live) { _hovering.Add(e.pointerId); Refresh(); } }
        public void OnPointerExit(PointerEventData e) { _hovering.Remove(e.pointerId); _pressing.Remove(e.pointerId); Refresh(); }
        public void OnPointerDown(PointerEventData e) { if (Live) { _pressing.Add(e.pointerId); Refresh(); } }
        public void OnPointerUp(PointerEventData e) { _pressing.Remove(e.pointerId); Refresh(); }

        private void OnDisable()
        {
            _hovering.Clear();
            _pressing.Clear();
            Refresh();
        }

        private void LateUpdate()
        {
            // A button turned non-interactable under the pointer goes dark at once.
            if ((_hovering.Count > 0 || _pressing.Count > 0) && !Live) OnDisable();
        }

        private void Refresh()
        {
            var level = _pressing.Count > 0 ? HoloStyle.PressGlow : _hovering.Count > 0 ? HoloStyle.HoverGlow : 0f;
            Show(level);
            transform.localScale = Vector3.one * (_pressing.Count > 0 ? HoloStyle.PressScale : 1f);
        }

        private void Show(float level)
        {
            if (_glow == null || Mathf.Approximately(level, _shown)) return;
            _shown = level;
            var fill = HoloStyle.Alpha(_tint, level);
            var edge = level > 0f ? HoloStyle.Alpha(_tint == HoloStyle.Cyan ? HoloStyle.BrightCyan : _tint, level >= HoloStyle.PressGlow ? 1f : .85f)
                : Color.clear;
            _glow.color = fill;
            _glow.BottomColor = fill;
            _glow.EdgeColor = edge;
            _glow.SetVerticesDirty();
        }
    }
}
