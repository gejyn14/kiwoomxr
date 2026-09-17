using System;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using SpatialTrading.Interfaces;
using SpatialTrading.Spatial;
using SpatialTrading.Market;
using TMPro;
using UnityEngine;

namespace SpatialTrading
{
    public sealed class FocusShell : MonoBehaviour
    {
        [SerializeField] private ChartComponent _chart;
        [SerializeField] private SecondaryComponent _secondary;
        [SerializeField] private SeatedLayout _layout;
        [SerializeField] private ShellInputSources _sources;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private MarketDataSession _market;
        private ShellActionDispatcher _dispatcher;
        private bool _hasFocus = true;
        private bool _paused;
        private bool _inputWasAvailable = true;
        private Guid _currentCorrelation = Guid.NewGuid();
        public ShellInputSources InputSources => _sources;
        public ShellState State => _dispatcher.State;
        // In Editor simulation, the XR session can be focused while another editor
        // panel/desktop window is active. Device builds retain application focus gating.
        public bool HasInteractionFocus => Application.isEditor && OVRManager.isHmdPresent
            ? OVRManager.hasInputFocus : _hasFocus;

        public void Configure(ChartComponent chart, SecondaryComponent secondary, SeatedLayout layout,
            ShellInputSources sources, TMP_Text status, MarketDataSession market)
        { _chart = chart; _secondary = secondary; _layout = layout; _sources = sources; _status = status; _market = market; }

        private void Awake()
        {
            _dispatcher = new ShellActionDispatcher(new ShellState(SyntheticCatalog.Samsung), SyntheticCatalog.Instruments);
            _dispatcher.Changed += OnContextChanged;
            if (_market != null) _market.Changed += OnMarketChanged;
            _dispatcher.Completed += RecordAction;
        }

        private void Start()
        {
            Application.targetFrameRate = 72;
            OnContextChanged(_dispatcher.State);
            _status.text = "터치로 선택 · 손잡이로 이동";
        }

        public void Activate(ShellControl control, InterfaceSource source)
        {
            if (!HasInteractionFocus || _paused || !isActiveAndEnabled) return;
            if (Application.isMobilePlatform && (!OVRManager.hasInputFocus || !OVRManager.tracker.isPositionTracked)) return;
            var revision = _dispatcher.State.Revision;
            ShellAction action;
            switch (control)
            {
                case ShellControl.SelectSamsung: action = new SelectInstrumentAction(SyntheticCatalog.Samsung, source, revision); break;
                case ShellControl.SelectSkHynix: action = new SelectInstrumentAction(SyntheticCatalog.SkHynix, source, revision); break;
                case ShellControl.OpenChart: action = new OpenComponentAction(ComponentKind.Chart, source, revision); break;
                case ShellControl.OpenOrderBook: action = new OpenComponentAction(ComponentKind.OrderBook, source, revision); break;
                case ShellControl.OpenPosition: action = new OpenComponentAction(ComponentKind.Position, source, revision); break;
                case ShellControl.CompareOther:
                    var other = _dispatcher.State.SelectedInstrument.Equals(SyntheticCatalog.Samsung) ? SyntheticCatalog.SkHynix : SyntheticCatalog.Samsung;
                    action = new CompareInstrumentAction(other, source, revision); break;
                case ShellControl.CloseSecondary: action = new CloseSecondaryAction(source, revision); break;
                case ShellControl.Recenter: _layout.Recenter(); return;
                default: return;
            }
            _currentCorrelation = action.CorrelationId;
            _dispatcher.Dispatch(action);
        }

        public void SetPreview(bool preview)
        { _currentCorrelation = Guid.NewGuid(); _market.Configure(preview); OnContextChanged(State); }

        private void OnContextChanged(ShellState state)
        { _market.Select(state, _currentCorrelation); Render(state); }
        private void OnMarketChanged() => Render(State);
        private void OnDestroy()
        { if (_market != null) _market.Changed -= OnMarketChanged; }

        private void Render(ShellState state)
        {
            foreach (var button in FindObjectsByType<ShellButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                button.RefreshSelection(state);
            // Renderer failures do not prevent other independently bound components from updating.
            try { _chart.RenderData(state.SelectedInstrument, _market.Quote(state.SelectedInstrument.Code), _market.Candles(state.SelectedInstrument.Code)); } catch (Exception error) { Debug.LogException(error, _chart); }
            try { _secondary.RenderData(state, _market.Book(state.SelectedInstrument.Code), _market.Quote(state.SelectedInstrument.Code),
                state.ComparisonInstrument == null ? null : _market.Quote(state.ComparisonInstrument.Code)); } catch (Exception error) { Debug.LogException(error, _secondary); }
        }

        private void RecordAction(ShellAction action, ActionResult result)
        {
            _status.text = result.Status == ActionStatus.Rejected ? "선택을 확인해 주세요" : "터치로 선택 · 손잡이로 이동";
            Debug.Log($"[ShellAction] type={action.Type} source={action.Source} correlation={action.CorrelationId} revision={result.ContextRevision} result={result.Reason}");
        }

        private void Update()
        {
            var available = HasInteractionFocus && !_paused;
            if (available == _inputWasAvailable) return;
            _inputWasAvailable = available;
            if (_status != null) _status.text = available ? "터치로 선택 · 손잡이로 이동" : "일시 정지 · 입력을 다시 시작하세요";
        }

        private void OnApplicationFocus(bool focused)
        {
            _hasFocus = focused;
            if (_status != null) _status.text = HasInteractionFocus && !_paused
                ? "터치로 선택 · 손잡이로 이동" : "일시 정지 · 입력을 다시 시작하세요";
        }
        private void OnApplicationPause(bool paused) => _paused = paused;
    }
}
