using SpatialTrading.Domain;
using SpatialTrading.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpatialTrading.Interfaces
{
    public enum ShellControl
    {
        SelectSamsung, SelectSkHynix, OpenChart, OpenOrderBook, OpenPosition,
        CompareOther, CloseSecondary, Recenter
    }

    /// <summary>The UI/SDK boundary produces an Action; it never changes financial state.</summary>
    public sealed class ShellButton : Button
    {
        [SerializeField] private FocusShell _shell;
        [SerializeField] private ShellControl _control;
        private int _lastActivationFrame = -1;
        private bool _selected;

        public void Configure(FocusShell shell, ShellControl control)
        { _shell = shell; _control = control; }

        public void RefreshSelection(ShellState state)
        {
            _selected = _control == ShellControl.SelectSamsung && state.SelectedInstrument.Equals(SyntheticCatalog.Samsung) ||
                _control == ShellControl.SelectSkHynix && state.SelectedInstrument.Equals(SyntheticCatalog.SkHynix) ||
                _control == ShellControl.OpenChart && !state.SecondaryComponent.HasValue ||
                _control == ShellControl.OpenOrderBook && state.SecondaryComponent == ComponentKind.OrderBook ||
                _control == ShellControl.OpenPosition && state.SecondaryComponent == ComponentKind.Position ||
                _control == ShellControl.CompareOther && state.SecondaryComponent == ComponentKind.Compare;
            DoStateTransition(currentSelectionState, true);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            if (!(targetGraphic is SpatialCardGraphic card)) return;
            var focused = state == SelectionState.Highlighted || state == SelectionState.Selected || state == SelectionState.Pressed;
            card.EdgeWidth = focused ? 3.5f : _selected ? 3 : 1;
            card.EdgeColor = focused ? Color.white : _selected ? new Color(.39f,.93f,.82f) : new Color(.38f,.48f,.64f,.8f);
            card.SetVerticesDirty();
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsActive() || !IsInteractable()) return;
            base.OnPointerClick(eventData);
            // One UI activation even if the SDK reports two completed pointers in the same frame.
            // This is presentation debounce only, never an order-idempotency mechanism.
            if (_lastActivationFrame == Time.frameCount) return;
            _lastActivationFrame = Time.frameCount;
            _shell.Activate(_control, _shell.InputSources.Resolve(eventData.pointerId));
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (!IsActive() || !IsInteractable()) return;
            base.OnSubmit(eventData);
            _shell.Activate(_control, InterfaceSource.UI);
        }
    }
}
