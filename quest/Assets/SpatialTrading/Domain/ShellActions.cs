using System;

namespace SpatialTrading.Domain
{
    public enum InterfaceSource { Hand, Controller, UI, Voice, Gemini }
    public enum ComponentKind { Chart, OrderBook, Position, Compare }
    public enum ApplicationMode { Focus }
    public enum ActionStatus { Applied, Rejected, Duplicate }

    public abstract class ShellAction
    {
        public Guid ActionId { get; }
        public Guid CorrelationId { get; }
        public InterfaceSource Source { get; }
        public DateTimeOffset OccurredAt { get; }
        public long ExpectedContextRevision { get; }
        public abstract string Type { get; }

        protected ShellAction(InterfaceSource source, long expectedRevision, Guid? actionId = null)
        {
            ActionId = actionId ?? Guid.NewGuid();
            CorrelationId = Guid.NewGuid();
            OccurredAt = DateTimeOffset.UtcNow;
            Source = source;
            ExpectedContextRevision = expectedRevision;
        }

        // A stable payload identity allows duplicate-ID mismatch rejection without serialization.
        internal abstract string PayloadKey { get; }

        protected static string InstrumentKey(InstrumentRef instrument) => instrument == null ? "<missing>" :
            $"{instrument.Id.Length}:{instrument.Id}{instrument.Code.Length}:{instrument.Code}{instrument.DisplayName.Length}:{instrument.DisplayName}";
    }

    public sealed class SelectInstrumentAction : ShellAction
    {
        public InstrumentRef Instrument { get; }
        public override string Type => "SELECT_INSTRUMENT";
        internal override string PayloadKey => InstrumentKey(Instrument);

        public SelectInstrumentAction(InstrumentRef instrument, InterfaceSource source,
            long expectedRevision, Guid? actionId = null) : base(source, expectedRevision, actionId)
        { Instrument = instrument; }
    }

    public sealed class OpenComponentAction : ShellAction
    {
        public ComponentKind Component { get; }
        public override string Type
        {
            get
            {
                switch (Component)
                {
                    case ComponentKind.Chart: return "OPEN_CHART";
                    case ComponentKind.OrderBook: return "OPEN_ORDERBOOK";
                    case ComponentKind.Position: return "OPEN_POSITION";
                    default: return "INVALID_ACTION";
                }
            }
        }
        internal override string PayloadKey => Component.ToString();

        public OpenComponentAction(ComponentKind component, InterfaceSource source,
            long expectedRevision, Guid? actionId = null) : base(source, expectedRevision, actionId)
        { Component = component; }
    }

    public sealed class CompareInstrumentAction : ShellAction
    {
        public InstrumentRef Instrument { get; }
        public override string Type => "COMPARE_INSTRUMENT";
        internal override string PayloadKey => InstrumentKey(Instrument);

        public CompareInstrumentAction(InstrumentRef instrument, InterfaceSource source,
            long expectedRevision, Guid? actionId = null) : base(source, expectedRevision, actionId)
        { Instrument = instrument; }
    }

    public sealed class CloseSecondaryAction : ShellAction
    {
        public override string Type => "CLOSE_COMPONENT";
        internal override string PayloadKey => "secondary";
        public CloseSecondaryAction(InterfaceSource source, long expectedRevision)
            : base(source, expectedRevision) { }
    }

    public sealed class ActionResult
    {
        public ActionStatus Status { get; }
        public string Reason { get; }
        public long ContextRevision { get; }
        public ActionResult(ActionStatus status, string reason, long revision)
        { Status = status; Reason = reason; ContextRevision = revision; }
    }
}
