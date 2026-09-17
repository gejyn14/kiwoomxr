using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    /// <summary>Pure presentation state. Financial truth is deliberately absent.</summary>
    public sealed class ShellState
    {
        public InstrumentRef SelectedInstrument { get; private set; }
        public InstrumentRef ComparisonInstrument { get; private set; }
        public ComponentKind? SecondaryComponent { get; private set; }
        public ApplicationMode CurrentMode => ApplicationMode.Focus;
        public long Revision { get; private set; }

        public ShellState(InstrumentRef initialInstrument)
        { SelectedInstrument = initialInstrument ?? throw new ArgumentNullException(nameof(initialInstrument)); }

        internal void Select(InstrumentRef instrument)
        {
            SelectedInstrument = instrument;
            if (instrument.Equals(ComparisonInstrument))
            {
                ComparisonInstrument = null;
                if (SecondaryComponent == ComponentKind.Compare) SecondaryComponent = null;
            }
            Revision++;
        }

        internal void Open(ComponentKind component)
        {
            SecondaryComponent = component == ComponentKind.Chart ? (ComponentKind?)null : component;
            Revision++;
        }

        internal void Compare(InstrumentRef instrument)
        {
            ComparisonInstrument = instrument;
            SecondaryComponent = ComponentKind.Compare;
            Revision++;
        }

        internal void CloseSecondary() { SecondaryComponent = null; Revision++; }
    }

    /// <summary>One business handler shared by every interface. No Unity or SDK dependency.</summary>
    public sealed class ShellActionDispatcher
    {
        private readonly Dictionary<string, InstrumentRef> _catalog;
        private readonly Dictionary<Guid, string> _processed = new Dictionary<Guid, string>();
        private readonly Queue<Guid> _processedOrder = new Queue<Guid>();
        private const int DedupeCapacity = 256; // Shell actions only; never an order execution ledger.
        public ShellState State { get; }
        public event Action<ShellState> Changed;
        public event Action<ShellAction, ActionResult> Completed;

        public ShellActionDispatcher(ShellState state, IEnumerable<InstrumentRef> catalog)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _catalog = new Dictionary<string, InstrumentRef>(StringComparer.Ordinal);
            foreach (var instrument in catalog) _catalog.Add(instrument.Id, instrument);
            if (!Resolve(state.SelectedInstrument, out _))
                throw new ArgumentException("Initial instrument must belong to the shell catalog.");
        }

        public ActionResult Dispatch(ShellAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.ActionId == Guid.Empty)
                return Complete(action, ActionStatus.Rejected, "INVALID_ACTION_ID");
            var key = action.Type + ":" + action.PayloadKey + ":" + action.ExpectedContextRevision + ":" + action.Source;
            if (_processed.TryGetValue(action.ActionId, out var previous))
                return Complete(action, previous == key ? ActionStatus.Duplicate : ActionStatus.Rejected,
                    previous == key ? "ALREADY_APPLIED" : "ACTION_ID_CONFLICT");
            if (!Enum.IsDefined(typeof(InterfaceSource), action.Source))
                return Complete(action, ActionStatus.Rejected, "INVALID_SOURCE");
            if (action.ExpectedContextRevision != State.Revision)
                return Complete(action, ActionStatus.Rejected, "CONTEXT_CHANGED");

            if (action is SelectInstrumentAction select)
            {
                if (!Resolve(select.Instrument, out var resolved))
                    return Complete(action, ActionStatus.Rejected, "INSTRUMENT_UNAVAILABLE");
                State.Select(resolved);
            }
            else if (action is OpenComponentAction open)
            {
                if (open.Component != ComponentKind.Chart && open.Component != ComponentKind.OrderBook &&
                    open.Component != ComponentKind.Position)
                    return Complete(action, ActionStatus.Rejected, "UNSUPPORTED_ACTION");
                State.Open(open.Component);
            }
            else if (action is CompareInstrumentAction compare)
            {
                if (!Resolve(compare.Instrument, out var resolved))
                    return Complete(action, ActionStatus.Rejected, "INSTRUMENT_UNAVAILABLE");
                if (resolved.Equals(State.SelectedInstrument))
                    return Complete(action, ActionStatus.Rejected, "SAME_INSTRUMENT");
                State.Compare(resolved);
            }
            else if (action is CloseSecondaryAction) State.CloseSecondary();
            else return Complete(action, ActionStatus.Rejected, "UNSUPPORTED_ACTION");

            _processed.Add(action.ActionId, key);
            _processedOrder.Enqueue(action.ActionId);
            if (_processedOrder.Count > DedupeCapacity) _processed.Remove(_processedOrder.Dequeue());
            Changed?.Invoke(State);
            return Complete(action, ActionStatus.Applied, "APPLIED");
        }

        private bool Resolve(InstrumentRef reference, out InstrumentRef canonical)
        {
            canonical = null;
            return reference != null && _catalog.TryGetValue(reference.Id, out canonical) &&
                   reference.Code == canonical.Code && reference.DisplayName == canonical.DisplayName;
        }

        private ActionResult Complete(ShellAction action, ActionStatus status, string reason)
        {
            var result = new ActionResult(status, reason, State.Revision);
            Completed?.Invoke(action, result);
            return result;
        }
    }
}
