using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using UnityEngine;

namespace SpatialTrading.Spread
{
    /// <summary>
    /// One per spread panel root. Follows its <see cref="Grabbable"/> while held, feeds the <see cref="FlickDetector"/>,
    /// and on release either raises <see cref="Dismissed"/> (a flick) or hands the drop point to the workspace, then
    /// eases into the snapped grid position. The bottom-right corner handle (its own Grabbable) resizes in whole cells.
    /// Rotation is always the workspace's: yaw only, pitch and roll 0.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpreadPanel : MonoBehaviour
    {
        public const float SnapSeconds = 0.12f;
        public const float DismissSeconds = 0.35f;
        public const float DismissDistance = 0.4f;
        public const float DismissScale = 0.6f;
        public const float AppearSeconds = 0.2f;
        /// <summary>Corner handle square edge in metres; its centre sits half of this in from the panel corner.</summary>
        public const float HandleSize = 0.034f;
        private const float BodyDepth = 0.02f;
        /// <summary>A flying panel moves this far toward the viewer (−z) and draws above its neighbours.</summary>
        public const float FlyLift = 0.03f;
        private const int FlySortBoost = 10;

        [SerializeField] private PanelId _id;
        [SerializeField] private SpreadWorkspace _workspace;
        [SerializeField] private HoloPanel _content;
        [SerializeField] private RectTransform _surface;
        [SerializeField] private CanvasGroup _fade;
        [SerializeField] private BoxCollider _body;
        [SerializeField] private BoxCollider _grabBar;
        [SerializeField] private Grabbable _grabbable;
        [SerializeField] private Transform _handle;
        [SerializeField] private BoxCollider _handleCollider;
        [SerializeField] private Grabbable _handleGrabbable;
        [SerializeField] private CanvasGroup _handleFade;

        private readonly FlickDetector _flick = new FlickDetector();
        private readonly List<(float t, Vector3 p)> _trail = new List<(float, Vector3)>();
        private PanelRect _rect;
        private bool _moving, _resizing;
        private Vector3 _resizeTopLeft;
        private Vector2Int _lastRequested;
        private Vector3 _snapFrom, _snapTo;
        private float _snapStart = -1f;
        private Coroutine _effect;
        private bool _flying;
        private Canvas _canvas;
        private int _baseSortingOrder;
        private Behaviour[] _grabInteractables;
        private static AudioClip _whoosh;

        /// <summary>A flick released the panel; the vector is the horizontal throw direction (world, normalised).</summary>
        public event Action<SpreadPanel, Vector3> Dismissed;

        public PanelId Id => _id;
        public HoloPanel Content => _content;
        public PanelRect Rect => _rect;
        public Transform Handle => _handle;
        public bool IsHeld => _moving || _resizing;
        public bool IsAnimating => _effect != null || _snapStart >= 0f;
        /// <summary>Mid dismiss flight: not grabbable, not re-laid out.</summary>
        public bool IsFlying => _flying;

        public void Configure(PanelId id, SpreadWorkspace workspace, HoloPanel content, RectTransform surface, CanvasGroup fade,
            BoxCollider body, BoxCollider grabBar, Grabbable grabbable, Transform handle, BoxCollider handleCollider,
            Grabbable handleGrabbable, CanvasGroup handleFade)
        {
            _id = id; _workspace = workspace; _content = content; _surface = surface; _fade = fade; _body = body;
            _grabBar = grabBar; _grabbable = grabbable; _handle = handle; _handleCollider = handleCollider;
            _handleGrabbable = handleGrabbable; _handleFade = handleFade;
        }

        public void Bind(SpreadWorkspace workspace) => _workspace = workspace;

        public bool IsHandle(Collider c) => c != null && c == _handleCollider;

        /// <summary>Sizes the surface, colliders and handle to the rect (cells × 50 mm). Safe in edit mode.</summary>
        public void ApplySize(PanelRect rect)
        {
            _rect = rect;
            var size = HoloStyle.SizeFor(rect);                       // millimetres
            float w = size.x * 0.001f, h = size.y * 0.001f;           // metres
            if (_surface != null && _surface.sizeDelta != size) _surface.sizeDelta = size;
            if (_body != null)
            {
                // Just behind the canvas plane, so the canvas (poke/ray UI) is always the nearer ray hit.
                _body.size = new Vector3(w, h, BodyDepth);
                _body.center = new Vector3(0f, 0f, BodyDepth * .5f + 0.001f);
            }
            if (_grabBar != null)
            {
                // A short strip on the top edge, in front of the canvas: the ray-grab target.
                _grabBar.size = new Vector3(Mathf.Min(w * .3f, .14f), .028f, .012f);
                _grabBar.center = new Vector3(0f, h * .5f - .006f, -.006f);
            }
            if (_handle != null && !_resizing) ResetHandle();
        }

        /// <summary>Places the panel at its snapped pose; eases there in <see cref="SnapSeconds"/> when animated.</summary>
        public void Place(PanelRect rect, Vector3 position, Quaternion rotation, bool animate)
        {
            ApplySize(rect);
            transform.rotation = rotation;
            if (_moving) return; // the hand owns the position until release
            if (!animate || !gameObject.activeInHierarchy || (transform.position - position).sqrMagnitude < 1e-8f)
            {
                transform.position = position;
                _snapStart = -1f;
                return;
            }
            _snapFrom = transform.position;
            _snapTo = position;
            _snapStart = Time.unscaledTime;
        }

        private void OnEnable()
        {
            if (_grabbable != null) _grabbable.WhenPointerEventRaised += OnBodyPointer;
            if (_handleGrabbable != null) _handleGrabbable.WhenPointerEventRaised += OnHandlePointer;
        }

        private void OnDisable()
        {
            if (_grabbable != null) _grabbable.WhenPointerEventRaised -= OnBodyPointer;
            if (_handleGrabbable != null) _handleGrabbable.WhenPointerEventRaised -= OnHandlePointer;
            _moving = _resizing = false;
            _snapStart = -1f;
            if (_effect != null) StopCoroutine(_effect);
            _effect = null;
            EndFlightState();
        }

        private void OnBodyPointer(PointerEvent e)
        {
            switch (e.Type)
            {
                case PointerEventType.Select: BeginMove(); break;
                case PointerEventType.Unselect: EndMove(); break;
                case PointerEventType.Cancel: EndMove(false); break;
            }
        }

        private void OnHandlePointer(PointerEvent e)
        {
            switch (e.Type)
            {
                case PointerEventType.Select: BeginResize(); break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel: EndResize(); break;
            }
        }

        /// <summary>Starts a move. Refused only mid-flight (the grab interactables are off then anyway); a fade-in is
        /// finished at once so the hand and the grid agree. Returns whether the move started.</summary>
        public bool BeginMove()
        {
            if (_moving || _flying || _workspace == null) return false;
            if (_effect != null) Settle();
            _moving = true;
            _snapStart = -1f;
            _trail.Clear();
            var p = transform.position;
            var t = Time.unscaledTime;
            _flick.Begin(t, p.x, p.y, p.z);
            _trail.Add((t, p));
            return true;
        }

        /// <param name="allowFlick">False when the grab was cancelled by the system rather than released.</param>
        public void EndMove(bool allowFlick = true)
        {
            if (!_moving) return;
            Sample();
            _moving = false;
            var t = Time.unscaledTime;
            if (allowFlick && _flick.ReleaseIsFlick(t)) Dismissed?.Invoke(this, ThrowDirection(t));
            else _workspace.Drop(_id, transform.position);
        }

        /// <summary>Starts a corner-handle resize (same rules as <see cref="BeginMove"/>).</summary>
        public bool BeginResize()
        {
            if (_resizing || _moving || _flying || _workspace == null) return false;
            if (_effect != null) Settle();
            _resizing = true;
            _lastRequested = new Vector2Int(_rect.Width, _rect.Height);
            var size = HoloStyle.SizeFor(_rect) * 0.001f;
            _resizeTopLeft = transform.position + transform.rotation * new Vector3(-size.x * .5f, size.y * .5f, 0f);
            return true;
        }

        public void EndResize()
        {
            if (!_resizing) return;
            _resizing = false;
            ResetHandle();
        }

        private void ResetHandle()
        {
            var size = HoloStyle.SizeFor(_rect) * 0.001f;
            _handle.localPosition = new Vector3(size.x * .5f - HandleSize * .5f, -size.y * .5f + HandleSize * .5f, -.004f);
            _handle.localRotation = Quaternion.identity;
            _handle.localScale = Vector3.one;
        }

        private void Sample()
        {
            var p = transform.position;
            var t = Time.unscaledTime;
            _flick.Sample(t, p.x, p.y, p.z);
            _trail.Add((t, p));
            while (_trail.Count > 2 && _trail[0].t < t - FlickDetector.WindowSeconds) _trail.RemoveAt(0);
        }

        private Vector3 ThrowDirection(float now)
        {
            var from = _trail.Count > 0 ? _trail[0].p : transform.position;
            var d = transform.position - from;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : transform.rotation * Vector3.right;
        }

        private void LateUpdate()
        {
            if (_workspace == null) return;
            if (_moving)
            {
                transform.rotation = _workspace.Rotation; // flat and front-facing even mid-grab
                Sample();
            }
            if (_resizing)
            {
                _handle.rotation = _workspace.Rotation;
                var d = Quaternion.Inverse(_workspace.Rotation) * (_handle.position - _resizeTopLeft);
                var cell = SpreadLayout.CellMeters;
                var want = new Vector2Int(Mathf.Max(1, Mathf.RoundToInt((d.x + HandleSize * .5f) / cell)),
                                          Mathf.Max(1, Mathf.RoundToInt((-d.y + HandleSize * .5f) / cell)));
                if (want != _lastRequested)
                {
                    _lastRequested = want;
                    _workspace.Resize(_id, want.x, want.y);
                }
            }
            if (_snapStart >= 0f)
            {
                var k = Mathf.Clamp01((Time.unscaledTime - _snapStart) / SnapSeconds);
                var e = 1f - (1f - k) * (1f - k) * (1f - k); // ease-out cubic
                transform.position = Vector3.LerpUnclamped(_snapFrom, _snapTo, e);
                if (k >= 1f) _snapStart = -1f;
            }
        }

        /// <summary>The dismiss flourish: flies 0.4 m along <paramref name="direction"/>, fades out and shrinks to 0.6×
        /// in 0.35 s with a procedural whoosh, then deactivates the panel.</summary>
        public void FlyAway(Vector3 direction)
        {
            _moving = _resizing = false;
            _snapStart = -1f;
            if (!gameObject.activeInHierarchy) return;
            if (_effect != null) StopCoroutine(_effect);
            BeginFlightState();
            AudioSource.PlayClipAtPoint(Whoosh, transform.position, .7f);
            _effect = StartCoroutine(Fly(direction.sqrMagnitude > 1e-6f ? direction.normalized : transform.rotation * Vector3.right));
        }

        /// <summary>No grabs during the flight (ISDK would otherwise move the transform under the animation and leave the
        /// panel off the grid), and the panel draws over its neighbours instead of interleaving with them.</summary>
        private void BeginFlightState()
        {
            _flying = true;
            SetGrabEnabled(false);
            if (Canvas != null) { _baseSortingOrder = _canvas.sortingOrder; _canvas.sortingOrder = _baseSortingOrder + FlySortBoost; }
        }

        private void EndFlightState()
        {
            if (!_flying) return;
            _flying = false;
            SetGrabEnabled(true);
            if (Canvas != null) _canvas.sortingOrder = _baseSortingOrder;
        }

        private Canvas Canvas => _canvas != null ? _canvas : (_surface != null ? _canvas = _surface.GetComponent<Canvas>() : null);

        /// <summary>The SDK's grab interactables on the body and the corner handle (not the canvas poke/ray ones).</summary>
        private void SetGrabEnabled(bool on)
        {
            if (_grabInteractables == null)
            {
                var found = new List<Behaviour>();
                foreach (var b in GetComponentsInChildren<MonoBehaviour>(true))
                    if (b is IInteractable && (_surface == null || !b.transform.IsChildOf(_surface))) found.Add(b);
                _grabInteractables = found.ToArray();
            }
            foreach (var b in _grabInteractables) if (b != null) b.enabled = on;
        }

        /// <summary>Re-activates a restored panel with a short fade and grow back to full size.</summary>
        public void Appear()
        {
            if (_effect != null) StopCoroutine(_effect);
            _effect = null;
            EndFlightState();
            gameObject.SetActive(true);
            _effect = StartCoroutine(FadeIn());
        }

        private IEnumerator Fly(Vector3 direction)
        {
            var start = transform.position;
            var toward = transform.rotation * Vector3.back; // toward the viewer
            var t0 = Time.unscaledTime;
            while (true)
            {
                var k = Mathf.Clamp01((Time.unscaledTime - t0) / DismissSeconds);
                var e = 1f - (1f - k) * (1f - k);
                transform.position = start + direction * (DismissDistance * e) + toward * (FlyLift * Mathf.Min(1f, k * 4f));
                transform.localScale = Vector3.one * Mathf.Lerp(1f, DismissScale, e);
                SetAlpha(1f - e);
                if (k >= 1f) break;
                yield return null;
            }
            _effect = null;
            EndFlightState();
            gameObject.SetActive(false);
            transform.localScale = Vector3.one;
            SetAlpha(1f);
        }

        private IEnumerator FadeIn()
        {
            var t0 = Time.unscaledTime;
            while (true)
            {
                var k = Mathf.Clamp01((Time.unscaledTime - t0) / AppearSeconds);
                var e = 1f - (1f - k) * (1f - k);
                transform.localScale = Vector3.one * Mathf.Lerp(DismissScale, 1f, e);
                SetAlpha(e);
                if (k >= 1f) break;
                yield return null;
            }
            transform.localScale = Vector3.one;
            SetAlpha(1f);
            _effect = null;
        }

        /// <summary>Clears any half-finished flourish (reset, or a grab during the fade-in).</summary>
        public void Settle()
        {
            if (_effect != null) StopCoroutine(_effect);
            _effect = null;
            EndFlightState();
            transform.localScale = Vector3.one;
            SetAlpha(1f);
        }

        private void SetAlpha(float a)
        {
            if (_fade != null) _fade.alpha = a;
            if (_handleFade != null) _handleFade.alpha = a;
        }

        /// <summary>0.25 s of white noise through a band-pass whose centre falls from 2.4 kHz to 300 Hz. No asset file.</summary>
        public static AudioClip Whoosh
        {
            get
            {
                if (_whoosh != null) return _whoosh;
                const int rate = 44100;
                const float seconds = .25f;
                var n = Mathf.RoundToInt(rate * seconds);
                var data = new float[n];
                var random = new System.Random(7);
                float low = 0f, band = 0f, peak = 1e-6f;
                const float damping = 1f / 1.6f; // 1/Q
                for (var i = 0; i < n; i++)
                {
                    var k = (float)i / n;
                    var fc = 2400f * Mathf.Pow(300f / 2400f, k);
                    var f = 2f * Mathf.Sin(Mathf.PI * fc / rate);
                    var x = (float)(random.NextDouble() * 2.0 - 1.0);
                    low += f * band;
                    var high = x - low - damping * band;
                    band += f * high;
                    var envelope = Mathf.Min(1f, k / .12f) * Mathf.Pow(1f - k, 1.5f);
                    data[i] = band * envelope;
                    peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                }
                for (var i = 0; i < n; i++) data[i] *= .6f / peak;
                _whoosh = AudioClip.Create("Whoosh", n, 1, rate, false);
                _whoosh.SetData(data, 0);
                return _whoosh;
            }
        }
    }
}
