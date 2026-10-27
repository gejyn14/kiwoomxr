using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using TMPro;
using UnityEngine;

namespace SpatialTrading.Spread
{
    /// <summary>
    /// Restore tray for dismissed panels. With hand tracking it opens 0.12 m above the left hand once the palm has faced
    /// up (palm normal · world up &gt; 0.7) for 0.4 s, and closes after the palm has been away for 0.6 s. Without hands
    /// (simulator controllers, Mac editor) the T key or the controller Y button pins the same tray ahead of the head and
    /// low. Each dismissed panel is an icon (short name, cyan edge); grabbing one and pulling it 0.1 m out restores the
    /// panel. "기본 배치로" sits at the end of the tray.
    /// </summary>
    public sealed class PalmTray : MonoBehaviour
    {
        public const float PalmUpDot = 0.7f;
        public const float ShowAfterSeconds = 0.4f;
        public const float HideAfterSeconds = 0.6f;
        public const float AboveHand = 0.12f;
        public const float PullOutMeters = 0.1f;
        public const float IconMm = 72f;
        public const float GapMm = 10f;
        public const float PadMm = 12f;
        public const float ResetWidthMm = 150f;
        public const float HeightMm = 96f;
        /// <summary>Pinned (no hands) position: ahead of the head and below the eye-plane panels.</summary>
        public static readonly Vector3 PinnedOffset = new Vector3(0f, -0.17f, 0.5f);

        [Serializable]
        public sealed class Icon
        {
            public PanelId Id;
            public Transform Root;
            public Collider Collider;
            public Grabbable Grabbable;
            [NonSerialized] public bool Held;
            [NonSerialized] public Vector3 Slot;
        }

        [SerializeField] private SpreadWorkspace _workspace;
        [SerializeField] private Transform _root;
        [SerializeField] private RectTransform _canvas;
        [SerializeField] private RectTransform _resetButton;
        [SerializeField] private UnityEngine.UI.Button _reset;
        [SerializeField] private BoxCollider _resetCollider;
        [SerializeField] private Icon[] _icons = new Icon[0];

        private readonly List<IHand> _hands = new List<IHand>();
        private float _nextHandSearch;
        private bool _pinned;
        private bool _palmShown;
        private float _palmUpSince = -1f, _palmAwaySince = -1f;
        private readonly Dictionary<Grabbable, Action<PointerEvent>> _handlers = new Dictionary<Grabbable, Action<PointerEvent>>();

        public bool IsShown => _root != null && _root.gameObject.activeSelf;
        public bool IsPinned => _pinned;
        public Transform TrayRoot => _root;

        public void Configure(SpreadWorkspace workspace, Transform root, RectTransform canvas, RectTransform resetButton,
            UnityEngine.UI.Button reset, BoxCollider resetCollider, Icon[] icons)
        {
            _workspace = workspace; _root = root; _canvas = canvas; _resetButton = resetButton; _reset = reset;
            _resetCollider = resetCollider; _icons = icons;
        }

        /// <summary>Short tray label for a panel.</summary>
        public static string Abbreviation(PanelId id)
        {
            switch (id)
            {
                case PanelId.ChartKiwoom: return "키움";
                case PanelId.ChartSamsung: return "삼성";
                case PanelId.ChartSkHynix: return "SK";
                case PanelId.ChartKospi: return "코스피";
                case PanelId.OrderBook: return "호가";
                case PanelId.News: return "뉴스";
                case PanelId.Balance: return "잔고";
                default: return "주문";
            }
        }

        private void Awake()
        {
            if (_reset != null)
            {
                var last = -1;
                _reset.onClick.AddListener(() =>
                {
                    if (last == Time.frameCount) return;
                    last = Time.frameCount;
                    if (_workspace != null) _workspace.Reset();
                });
            }
            for (var i = 0; i < _icons.Length; i++)
            {
                var index = i;
                var icon = _icons[i];
                if (icon?.Grabbable == null) continue;
                Action<PointerEvent> handler = e =>
                {
                    if (e.Type == PointerEventType.Select) BeginIconDrag(index);
                    else if (e.Type == PointerEventType.Unselect) EndIconDrag(index);
                    else if (e.Type == PointerEventType.Cancel) CancelIconDrag(index);
                };
                _handlers[icon.Grabbable] = handler;
                icon.Grabbable.WhenPointerEventRaised += handler;
            }
            if (_workspace != null) _workspace.LayoutChanged += Refresh;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var pair in _handlers) if (pair.Key != null) pair.Key.WhenPointerEventRaised -= pair.Value;
            if (_workspace != null) _workspace.LayoutChanged -= Refresh;
        }

        /// <summary>T key / Y button: pins the tray ahead of the head, or closes it.</summary>
        public void Toggle()
        {
            if (_pinned) Hide();
            else Pin();
        }

        public void Pin()
        {
            _pinned = true;
            Show();
            PlacePinned();
        }

        public void Hide()
        {
            _pinned = false;
            _palmShown = false;
            _palmUpSince = _palmAwaySince = -1f;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            Refresh();
        }

        private void PlacePinned()
        {
            if (_workspace == null || _workspace.Head == null) return;
            var head = _workspace.Head;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var yaw = forward.sqrMagnitude > 0.01f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : _workspace.Rotation;
            _root.SetPositionAndRotation(head.position + yaw * PinnedOffset, yaw);
        }

        private void Update()
        {
            if (OVRInput.GetDown(OVRInput.Button.Four)) Toggle(); // controller Y
            if (_pinned) return;
            var palmUp = TryPalm(out var palm);
            var now = Time.unscaledTime;
            if (palmUp)
            {
                _palmAwaySince = -1f;
                if (_palmUpSince < 0f) _palmUpSince = now;
                if (!_palmShown && now - _palmUpSince >= ShowAfterSeconds) { _palmShown = true; Show(); }
            }
            else
            {
                _palmUpSince = -1f;
                if (_palmShown)
                {
                    if (_palmAwaySince < 0f) _palmAwaySince = now;
                    if (now - _palmAwaySince >= HideAfterSeconds) Hide();
                }
            }
            if (_palmShown && palmUp && !AnyHeld()) FacePalm(palm);
        }

        private void FacePalm(Vector3 palm)
        {
            var position = palm + Vector3.up * AboveHand;
            var head = _workspace != null && _workspace.Head != null ? _workspace.Head.position : position - Vector3.forward;
            var toward = Vector3.ProjectOnPlane(position - head, Vector3.up);
            var rotation = toward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toward.normalized, Vector3.up) : _workspace.Rotation;
            _root.SetPositionAndRotation(position, rotation); // flat: yaw only
        }

        /// <summary>Left hand palm facing up, from wrist/index/middle/pinky joints (no handedness-specific palm axis).</summary>
        private bool TryPalm(out Vector3 palm)
        {
            palm = default;
            if (Time.unscaledTime >= _nextHandSearch && _hands.Count == 0)
            {
                _nextHandSearch = Time.unscaledTime + 1f;
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (behaviour is IHand hand && hand.Handedness == Handedness.Left) _hands.Add(hand);
            }
            foreach (var hand in _hands)
            {
                if (hand == null || !hand.IsConnected || !hand.IsHighConfidence) continue;
                if (!hand.GetJointPose(HandJointId.HandWristRoot, out var wrist) ||
                    !hand.GetJointPose(HandJointId.HandIndex1, out var index) ||
                    !hand.GetJointPose(HandJointId.HandMiddle1, out var middle) ||
                    !hand.GetJointPose(HandJointId.HandPinky1, out var pinky)) continue;
                // Left hand: across (index - pinky) × forward (middle - wrist) points out of the palm.
                var normal = Vector3.Cross(index.position - pinky.position, middle.position - wrist.position);
                if (normal.sqrMagnitude < 1e-8f) continue;
                palm = (wrist.position + middle.position) * .5f;
                return Vector3.Dot(normal.normalized, Vector3.up) > PalmUpDot;
            }
            return false;
        }

        /// <summary>Lays the dismissed-panel icons out left to right with "기본 배치로" last.</summary>
        public void Refresh()
        {
            if (_workspace == null || _root == null) return;
            var shown = new List<Icon>();
            foreach (var icon in _icons)
            {
                if (icon?.Root == null) continue;
                var on = _workspace.Layout.IsDismissed(icon.Id);
                if (icon.Root.gameObject.activeSelf != on) icon.Root.gameObject.SetActive(on);
                if (on) shown.Add(icon);
            }
            var width = 2 * PadMm + shown.Count * (IconMm + GapMm) + ResetWidthMm;
            if (_canvas != null) _canvas.sizeDelta = new Vector2(width, HeightMm);
            var left = -width * .5f + PadMm;
            for (var i = 0; i < shown.Count; i++)
            {
                var x = left + i * (IconMm + GapMm) + IconMm * .5f;
                shown[i].Slot = new Vector3(x * .001f, 0f, -.004f);
                if (!shown[i].Held) { shown[i].Root.localPosition = shown[i].Slot; shown[i].Root.localRotation = Quaternion.identity; }
            }
            var resetX = width * .5f - PadMm - ResetWidthMm * .5f;
            if (_resetButton != null) _resetButton.anchoredPosition = new Vector2(resetX, 0f);
            if (_resetCollider != null)
            {
                _resetCollider.center = new Vector3(resetX * .001f, 0f, 0f);
                _resetCollider.size = new Vector3(ResetWidthMm * .001f, (HeightMm - 2 * PadMm) * .001f, .01f);
            }
        }

        private bool AnyHeld()
        {
            foreach (var icon in _icons) if (icon != null && icon.Held) return true;
            return false;
        }

        public bool TryGetIcon(Collider c, out int index)
        {
            for (index = 0; index < _icons.Length; index++)
                if (_icons[index] != null && _icons[index].Collider == c && IsShown) return true;
            index = -1;
            return false;
        }

        public bool IsResetCollider(Collider c) => c != null && c == _resetCollider && IsShown;

        public void BeginIconDrag(int index)
        {
            if (index < 0 || index >= _icons.Length || _icons[index] == null) return;
            _icons[index].Held = true;
        }

        /// <summary>Released: pulled at least 0.1 m out of its slot restores the panel, otherwise the icon snaps back.</summary>
        public void EndIconDrag(int index)
        {
            if (index < 0 || index >= _icons.Length || _icons[index] == null) return;
            var icon = _icons[index];
            if (!icon.Held) return;
            icon.Held = false;
            var slot = _root.TransformPoint(icon.Slot);
            if (Vector3.Distance(icon.Root.position, slot) >= PullOutMeters && _workspace != null) _workspace.Restore(icon.Id);
            else Refresh();
            icon.Root.localPosition = icon.Slot;
            icon.Root.localRotation = Quaternion.identity;
        }

        private void CancelIconDrag(int index)
        {
            if (index < 0 || index >= _icons.Length || _icons[index] == null) return;
            _icons[index].Held = false;
            Refresh();
        }
    }
}
