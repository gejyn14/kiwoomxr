using SpatialTrading.Domain;
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

        public void Configure(FocusShell shell, ShellControl control)
        { _shell = shell; _control = control; }

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
