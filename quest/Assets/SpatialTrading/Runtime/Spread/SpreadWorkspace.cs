using System;
using System.Collections.Generic;
using System.Linq;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using SpatialTrading.Market;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevices = UnityEngine.XR.InputDevices;
using DomainPlane = SpatialTrading.Domain.Plane;

namespace SpatialTrading.Spread
{
    /// <summary>
    /// The trader spread: owns the <see cref="SpreadLayout"/>, pins the workspace origin to the head position and yaw,
    /// routes chart selection into the <see cref="OrderTicket"/>, and redraws the panels from <see cref="MarketDataSession"/>.
    /// Eye plane: 1.10 m ahead at head height. Hand plane: 0.60 m ahead, 0.45 m below. Every panel faces the workspace
    /// forward (yaw only). Nothing here sends an order.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SpreadWorkspace : MonoBehaviour
    {
        public const float EyeDistance = 1.10f;
        public const float HandDistance = 0.60f;
        public const float HandHeight = -0.45f;
        private const float MouseClickPixels = 6f;
        private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

        public static readonly PanelId[] ChartIds = { PanelId.ChartKiwoom, PanelId.ChartSamsung, PanelId.ChartSkHynix, PanelId.ChartKospi };

        [SerializeField] private Transform _head;
        [SerializeField] private Camera _eye;
        [SerializeField] private MarketDataSession _market;
        [SerializeField] private PalmTray _tray;
        [SerializeField] private SpreadPanel[] _panels = new SpreadPanel[0];

        private readonly SpreadLayout _layout = SpreadLayout.TraderDefault();
        private readonly OrderTicket _ticket = new OrderTicket();
        private readonly Dictionary<PanelId, SpreadPanel> _byId = new Dictionary<PanelId, SpreadPanel>();
        private readonly Dictionary<PanelId, string> _intervals = new Dictionary<PanelId, string>();
        private OrderPreview _preview; // cached once per 매수 준비; never re-prepared on render
        private PanelId _selected = PanelId.ChartSamsung;
        private TradingSession _session = TradingSession.Closed;
        private float _nextTick;
        private int _newsMinute = -1;
        private Vector3 _originPosition;
        private Quaternion _originRotation = Quaternion.identity;
        private bool _placed;
        private XRInputSubsystem _input;
        private int _readyAfterFrame;
        /// <summary>No XR: frames the camera must hold still before the workspace pins to it.</summary>
        private const int SettleFrames = 10;
        private int _stillFrames;
        private bool _started;

        private enum MouseDrag { None, Move, Resize, Icon }
        private MouseDrag _drag;
        private SpreadPanel _dragPanel;
        private int _dragIcon = -1;
        private Transform _dragTarget;
        private UnityEngine.Plane _dragPlane;
        private Vector3 _dragOffset;
        private Vector2 _pressAt;

        /// <summary>Raised after any placement, dismissal, restore or reset.</summary>
        public event Action LayoutChanged;

        public SpreadLayout Layout => _layout;
        public PanelId Selected => _selected;
        public InstrumentRef SelectedInstrument => InstrumentFor(_selected);
        public OrderTicket Ticket => _ticket;
        public OrderPreview CachedPreview => _preview;
        public TradingSession Session => _session;
        public Quaternion Rotation => _originRotation;
        public Vector3 Origin => _originPosition;
        public Transform Head => _head;
        public Camera Eye => _eye;
        public MarketDataSession Market => _market;
        public IReadOnlyList<SpreadPanel> Panels => _panels;
        public string IntervalOf(PanelId id) => _intervals.TryGetValue(id, out var i) ? i : ChartPanel.Daily;
        public SpreadPanel Panel(PanelId id) => _byId.TryGetValue(id, out var p) ? p : null;

        public void Configure(Transform head, Camera eye, MarketDataSession market, PalmTray tray, SpreadPanel[] panels)
        { _head = head; _eye = eye; _market = market; _tray = tray; _panels = panels; }

        public static bool IsChart(PanelId id) => Array.IndexOf(ChartIds, id) >= 0;

        public static InstrumentRef InstrumentFor(PanelId id)
        {
            switch (id)
            {
                case PanelId.ChartKiwoom: return TraderCatalog.Kiwoom;
                case PanelId.ChartSamsung: return TraderCatalog.Samsung;
                case PanelId.ChartSkHynix: return TraderCatalog.SkHynix;
                case PanelId.ChartKospi: return TraderCatalog.Kospi;
                default: return null;
            }
        }

        /// <summary>Panel centre in workspace space (x right, y up, z forward; metres from the head).</summary>
        public static Vector3 LocalCenter(PanelRect r)
        {
            var x = (r.X + r.Width * .5f) * SpreadLayout.CellMeters;
            var y = (r.Y + r.Height * .5f) * SpreadLayout.CellMeters;
            return r.Plane == DomainPlane.Eye ? new Vector3(x, y, EyeDistance) : new Vector3(x, y + HandHeight, HandDistance);
        }

        public Vector3 WorldCenter(PanelRect r) => _originPosition + _originRotation * LocalCenter(r);

        private static DateTimeOffset NowKst => DateTimeOffset.UtcNow.ToOffset(Kst);

        private void Awake()
        {
            foreach (var id in ChartIds) _intervals[id] = ChartPanel.Daily;
            foreach (var panel in _panels)
            {
                if (panel == null) continue;
                _byId[panel.Id] = panel;
                panel.Dismissed += (p, direction) => Dismiss(p.Id, direction);
                var id = panel.Id;
                switch (panel.Content)
                {
                    case ChartPanel chart:
                        chart.Selected += () => SelectChart(id);
                        chart.IntervalChanged += interval => SetInterval(id, interval);
                        break;
                    case OrderPanel order:
                        order.TypeChosen += ChooseType;
                        order.PrepareRequested += Prepare;
                        break;
                }
            }
            if (_market != null) _market.Changed += Render;
        }

        private void OnDestroy()
        {
            if (_market != null) _market.Changed -= Render;
            if (_input != null) _input.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _input = null;
        }

        private void Start()
        {
            Application.targetFrameRate = 72;
            _session = TradingSessions.SessionAt(NowKst);
            _ticket.Select(SelectedInstrument, _session);
            // Provisional placement from the current camera pose (Mac editor backup has no XR at all); once an XR
            // runtime reports a tracked head (headset or Meta XR Simulator), LateUpdate pins the workspace to it.
            PlaceAt(false);
            _started = true;
            if (_market != null) _market.SubscribeAll(Guid.NewGuid());
            Render();
        }

        // ---------- origin ----------

        /// <summary>Pins the workspace origin to the current head position and yaw, then lays every panel out again.</summary>
        public void Recenter()
        {
            if (_head == null) return;
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(_head.up, Vector3.up) * -1f; // looking straight down/up
            if (forward.sqrMagnitude < 0.01f) return;
            _originPosition = _head.position;
            _originRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _placed = true;
            PlaceAll(false);
        }

        /// <summary>True once the origin is pinned: a tracked XR head, an explicit <see cref="Recenter"/>, or (no XR) a camera
        /// that has held still for <see cref="SettleFrames"/> frames.</summary>
        public bool IsPlaced => _placed;

        private void PlaceAt(bool placed)
        {
            Recenter();
            _placed = placed;
        }

        private void LateUpdate()
        {
            if (_placed) return;
            if (!XRSettings.isDeviceActive)
            {
                // No XR runtime: follow the camera while the rig settles, then pin once it has held still for
                // SettleFrames frames (or as soon as a mouse drag starts), so later snaps and flights are never re-laid out.
                if (_head == null) return;
                if (_drag != MouseDrag.None) { _placed = true; return; }
                var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
                var turned = forward.sqrMagnitude > 0.01f && Vector3.Angle(forward, _originRotation * Vector3.forward) > .1f;
                if ((_head.position - _originPosition).sqrMagnitude > 1e-6f || turned)
                {
                    PlaceAt(false);
                    _stillFrames = 0;
                }
                else if (++_stillFrames >= SettleFrames) _placed = true;
                return;
            }
            var input = OVRManager.GetCurrentInputSubsystem();
            if (input != _input)
            {
                if (_input != null) _input.trackingOriginUpdated -= OnTrackingOriginUpdated;
                _input = input;
                if (_input != null) _input.trackingOriginUpdated += OnTrackingOriginUpdated;
                OnTrackingOriginUpdated(_input);
            }
            // Wait for floor origin and a tracked head pose already applied to the rig (same rule as the old shell).
            if (_head == null || _input == null || !_input.running || Time.frameCount < _readyAfterFrame ||
                _input.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out var position) ||
                Vector3.Distance(_head.localPosition, position) > 0.001f) return;
            Recenter();
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem input)
        { _placed = false; _readyAfterFrame = Time.frameCount + 2; }

        private void PlaceAll(bool animate)
        {
            foreach (var panel in _panels)
            {
                // Dismissed or mid-flight panels keep their own motion; Restore and Reset place them again.
                if (panel == null || _layout.IsDismissed(panel.Id) || panel.IsFlying) continue;
                panel.Place(_layout[panel.Id], WorldCenter(_layout[panel.Id]), _originRotation, animate);
            }
        }

        // ---------- selection and order ticket ----------

        /// <summary>Selects a chart: its instrument drives the order book, the order ticket and the news ranking.</summary>
        public void SelectChart(PanelId id)
        {
            if (!IsChart(id)) return;
            if (id != _selected)
            {
                _selected = id;
                _ticket.Select(SelectedInstrument, _session);
                _preview = null;
            }
            Render();
        }

        public void SetInterval(PanelId id, string interval)
        {
            if (!IsChart(id) || (interval != ChartPanel.Daily && interval != ChartPanel.FourHour)) return;
            _intervals[id] = interval;
            RenderPanel(id);
        }

        private void ChooseType(OrderType type)
        {
            var before = _ticket.Type;
            if (_ticket.TryChooseType(type) && type != before) _preview = null;
            RenderPanel(PanelId.Order);
        }

        /// <summary>매수 준비: prepares once and caches the preview. "확인" on the card sends nothing anywhere.</summary>
        private void Prepare()
        {
            var code = SelectedInstrument.Code;
            var ask = _market?.Book(code)?.Value?.Asks.FirstOrDefault(l => l.Rank == 1);
            decimal? bestAsk = ask != null && ask.Price > 0m ? ask.Price : (decimal?)null;
            var last = _market?.Quote(code)?.Value?.LastPrice;
            _preview = _ticket.TryPrepare(bestAsk, last, out var preview) ? preview : null;
            RenderPanel(PanelId.Order);
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 1f;
                Tick();
            }
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) Reset();
                if (keyboard.tKey.wasPressedThisFrame && _tray != null) _tray.Toggle();
                if (keyboard.digit1Key.wasPressedThisFrame) SelectChart(ChartIds[0]);
                if (keyboard.digit2Key.wasPressedThisFrame) SelectChart(ChartIds[1]);
                if (keyboard.digit3Key.wasPressedThisFrame) SelectChart(ChartIds[2]);
                if (keyboard.digit4Key.wasPressedThisFrame) SelectChart(ChartIds[3]);
            }
            if (!XRSettings.isDeviceActive) UpdateMouse();
        }

        /// <summary>1 s session clock (KST). A session change re-selects the same instrument: the ticket has no SetSession.</summary>
        private void Tick()
        {
            var now = NowKst;
            var session = TradingSessions.SessionAt(now);
            if (session != _session)
            {
                _session = session;
                _ticket.Select(SelectedInstrument, _session); // resets the chosen type; accepted
                _preview = null;
                RenderPanel(PanelId.Order);
            }
            var minute = now.Hour * 60 + now.Minute;
            if (minute != _newsMinute)
            {
                _newsMinute = minute; // news time text is relative to "today" (KST)
                RenderPanel(PanelId.News);
            }
        }

        // ---------- layout operations ----------

        /// <summary>Drops a released panel at a world point: plane by depth, then grid snap in <see cref="SpreadLayout"/>.</summary>
        public PanelRect Drop(PanelId id, Vector3 world)
        {
            var local = Quaternion.Inverse(_originRotation) * (world - _originPosition);
            var plane = local.z < (EyeDistance + HandDistance) * .5f ? DomainPlane.Hand : DomainPlane.Eye;
            var y = plane == DomainPlane.Eye ? local.y : local.y - HandHeight;
            var rect = _layout.Drop(id, plane, local.x, y);
            Apply(id, true);
            return rect;
        }

        /// <summary>Whole-cell resize from the bottom-right handle; keeps the top-left corner when that spot is free.</summary>
        public PanelRect Resize(PanelId id, int width, int height)
        {
            var before = _layout[id];
            var top = before.Y + before.Height;
            var r = _layout.Resize(id, width, height);
            if (r.Width != before.Width || r.Height != before.Height)
                r = _layout.Drop(id, r.Plane, (before.X + r.Width * .5f) * SpreadLayout.CellMeters,
                    (top - r.Height * .5f) * SpreadLayout.CellMeters);
            Apply(id, true);
            return r;
        }

        /// <summary>Throws a panel away (flick). The order panel cancels any preflight first: no hidden order state.</summary>
        public void Dismiss(PanelId id, Vector3 direction)
        {
            if (_layout.IsDismissed(id)) return;
            _layout.Dismiss(id);
            if (id == PanelId.Order)
            {
                _ticket.Cancel();
                _preview = null;
            }
            var panel = Panel(id);
            if (panel != null) panel.FlyAway(direction);
            LayoutChanged?.Invoke();
        }

        /// <summary>Throws a panel away toward its own side of the spread (API and editor use).</summary>
        public void Dismiss(PanelId id)
        {
            var local = LocalCenter(_layout[id]);
            Dismiss(id, _originRotation * (local.x >= 0f ? Vector3.right : Vector3.left));
        }

        public PanelRect Restore(PanelId id)
        {
            var wasDismissed = _layout.IsDismissed(id);
            var rect = _layout.Restore(id);
            var panel = Panel(id);
            if (panel != null && wasDismissed)
            {
                panel.Place(rect, WorldCenter(rect), _originRotation, false);
                panel.Appear();
            }
            RenderPanel(id);
            LayoutChanged?.Invoke();
            return rect;
        }

        /// <summary>트레이더 기본: every panel back, default sizes and places. (Play mode only; Unity also calls Reset in edit mode.)</summary>
        public void Reset()
        {
            if (!Application.isPlaying || !_started) return;
            var dismissed = new HashSet<PanelId>(_layout.Dismissed);
            _layout.Reset();
            foreach (var panel in _panels)
            {
                if (panel == null) continue;
                if (dismissed.Contains(panel.Id) || !panel.gameObject.activeSelf)
                {
                    panel.Place(_layout[panel.Id], WorldCenter(_layout[panel.Id]), _originRotation, false);
                    panel.Appear();
                }
                else
                {
                    panel.Settle();
                    panel.Place(_layout[panel.Id], WorldCenter(_layout[panel.Id]), _originRotation, true);
                }
            }
            if (_tray != null) _tray.Hide();
            Render();
            LayoutChanged?.Invoke();
        }

        private void Apply(PanelId id, bool animate)
        {
            var panel = Panel(id);
            if (panel != null && !_layout.IsDismissed(id))
                panel.Place(_layout[id], WorldCenter(_layout[id]), _originRotation, animate);
            RenderPanel(id);
            LayoutChanged?.Invoke();
        }

        // ---------- data ----------

        public void SetPreview(bool preview)
        {
            if (_market == null) return;
            _market.Configure(preview);
            _market.SubscribeAll(Guid.NewGuid());
            Render();
        }

        public void Render()
        {
            foreach (var panel in _panels)
                if (panel != null) RenderPanel(panel.Id);
        }

        private void RenderPanel(PanelId id)
        {
            var panel = Panel(id);
            if (panel == null || _market == null || _layout.IsDismissed(id) || !panel.gameObject.activeSelf) return;
            var rect = _layout[id];
            // A renderer failure never blocks the other panels.
            try
            {
                switch (panel.Content)
                {
                    case ChartPanel chart:
                    {
                        var instrument = InstrumentFor(id);
                        var interval = IntervalOf(id);
                        var series = interval == ChartPanel.FourHour ? _market.FourHour(instrument.Code) : _market.Daily(instrument.Code);
                        var view = ChartHeaderView.From(instrument, series, _market.Quote(instrument.Code), interval,
                            _market.MarketLabel(instrument.Code, interval));
                        chart.Render(view, series?.Value, interval, rect);
                        chart.SetSelected(id == _selected);
                        break;
                    }
                    case OrderBookPanel book:
                    {
                        var instrument = SelectedInstrument;
                        book.Render(OrderBookView.From(_market.Book(instrument.Code), _market.Quote(instrument.Code),
                            SpreadLayout.BookDepthFor(rect), instrument, _market.MarketLabel(instrument.Code)), rect);
                        break;
                    }
                    case NewsPanel news:
                    {
                        var feed = _market.News();
                        news.Render(NewsView.Rank(feed, SelectedInstrument.Code, NowKst, NewsPanel.RowsFor(rect)), NewsView.Status(feed), rect);
                        break;
                    }
                    case BalancePanel balance:
                        balance.Render(BalanceView.From(_market.Balance()), rect);
                        break;
                    case OrderPanel order:
                    {
                        if (_ticket.State != TicketState.Preflight) _preview = null;
                        var code = SelectedInstrument.Code;
                        order.Render(OrderView.From(_ticket, _market.Book(code), _session, _market.Quote(code)), rect, _preview);
                        break;
                    }
                }
            }
            catch (Exception error) { Debug.LogException(error, panel); }
        }

        // ---------- editor / Mac backup: mouse ----------

        private void UpdateMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null || _eye == null || !_eye.isActiveAndEnabled) return;
            var at = mouse.position.ReadValue();
            var ray = _eye.ScreenPointToRay(at);
            if (mouse.leftButton.wasPressedThisFrame && _drag == MouseDrag.None)
            {
                if (!Physics.Raycast(ray, out var hit, 6f)) return;
                _pressAt = at;
                var panel = hit.collider.GetComponentInParent<SpreadPanel>();
                if (panel != null && panel.IsHandle(hit.collider))
                {
                    if (!panel.BeginResize()) return; // mid-flight: nothing to drag
                    _drag = MouseDrag.Resize; _dragPanel = panel; _dragTarget = panel.Handle;
                }
                else if (panel != null)
                {
                    if (!panel.BeginMove()) return;
                    _drag = MouseDrag.Move; _dragPanel = panel; _dragTarget = panel.transform;
                }
                else if (_tray != null && _tray.TryGetIcon(hit.collider, out var icon))
                {
                    _drag = MouseDrag.Icon; _dragIcon = icon; _dragTarget = hit.collider.transform; _tray.BeginIconDrag(icon);
                }
                else if (_tray != null && _tray.IsResetCollider(hit.collider)) { Reset(); return; }
                else return;
                _dragPlane = new UnityEngine.Plane(_dragTarget.forward, _dragTarget.position);
                _dragOffset = _dragPlane.Raycast(ray, out var d) ? _dragTarget.position - ray.GetPoint(d) : Vector3.zero;
                return;
            }
            if (_drag == MouseDrag.None) return;
            if (mouse.leftButton.isPressed)
            {
                if (_dragPlane.Raycast(ray, out var d)) _dragTarget.position = ray.GetPoint(d) + _dragOffset;
                return;
            }
            var click = (at - _pressAt).magnitude < MouseClickPixels;
            switch (_drag)
            {
                case MouseDrag.Move:
                    _dragPanel.EndMove();
                    if (click) SelectChart(_dragPanel.Id); // charts only; ignored elsewhere
                    break;
                case MouseDrag.Resize: _dragPanel.EndResize(); break;
                case MouseDrag.Icon: _tray.EndIconDrag(_dragIcon); break;
            }
            _drag = MouseDrag.None; _dragPanel = null; _dragTarget = null; _dragIcon = -1;
        }
    }
}
