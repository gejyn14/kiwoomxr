using System;
using SpatialTrading.Components;
using SpatialTrading.Domain;
using SpatialTrading.Interfaces;
using SpatialTrading.Spatial;
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
        private ShellActionDispatcher _dispatcher;
        private bool _hasFocus = true;
        private bool _paused;
        public ShellInputSources InputSources => _sources;
        public ShellState State => _dispatcher.State;
        // In Editor simulation, the XR session can be focused while another editor
        // panel/desktop window is active. Device builds retain application focus gating.
        public bool HasInteractionFocus => Application.isEditor && OVRManager.isHmdPresent
            ? OVRManager.hasInputFocus : _hasFocus;

        public void Configure(ChartComponent chart, SecondaryComponent secondary, SeatedLayout layout,
            ShellInputSources sources, TMP_Text status)
        { _chart = chart; _secondary = secondary; _layout = layout; _sources = sources; _status = status; }

        private void Awake()
        {
            _dispatcher = new ShellActionDispatcher(new ShellState(SyntheticCatalog.Samsung), SyntheticCatalog.Instruments);
            _dispatcher.Changed += Render;
            _dispatcher.Completed += RecordAction;
        }

        private void Start()
        {
            Application.targetFrameRate = 72;
            Render(_dispatcher.State);
            _status.text = "FOCUS · 합성 데이터 · 거래 기능 없음";
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
            _dispatcher.Dispatch(action);
        }

        private void Render(ShellState state)
        {
            // Renderer failures do not prevent other independently bound components from updating.
            try { _chart.Render(state); } catch (Exception error) { Debug.LogException(error, _chart); }
            try { _secondary.Render(state); } catch (Exception error) { Debug.LogException(error, _secondary); }
        }

        private void RecordAction(ShellAction action, ActionResult result)
        {
            _status.text = $"{action.Type} · {action.Source} · {result.Reason}";
            Debug.Log($"[ShellAction] type={action.Type} source={action.Source} correlation={action.CorrelationId} revision={result.ContextRevision} result={result.Reason}");
        }

        private void OnApplicationFocus(bool focused)
        {
            _hasFocus = focused;
            if (_status != null) _status.text = HasInteractionFocus && !_paused
                ? "FOCUS · 합성 데이터 · 거래 기능 없음" : "일시 정지 · 입력을 다시 시작하세요";
        }
        private void OnApplicationPause(bool paused) => _paused = paused;
    }
}
