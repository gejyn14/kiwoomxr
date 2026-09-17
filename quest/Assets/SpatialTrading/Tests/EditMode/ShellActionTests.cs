using System;
using NUnit.Framework;
using SpatialTrading.Domain;

namespace SpatialTrading.Tests
{
    public sealed class ShellActionTests
    {
        private static ShellActionDispatcher NewDispatcher() =>
            new ShellActionDispatcher(new ShellState(SyntheticCatalog.Samsung), SyntheticCatalog.Instruments);

        [Test]
        public void MissingFixtureCannotBecomeAnotherInstrumentsPrice()
        {
            Assert.Throws<ArgumentException>(() => SyntheticCatalog.ReferencePrice(null));
            Assert.Throws<ArgumentException>(() => SyntheticCatalog.ReferencePrice(new InstrumentRef("unknown", "123456", "Unknown")));
        }

        [TestCase(InterfaceSource.Hand)]
        [TestCase(InterfaceSource.Controller)]
        [TestCase(InterfaceSource.UI)]
        [TestCase(InterfaceSource.Voice)]
        [TestCase(InterfaceSource.Gemini)]
        public void EveryInterfaceUsesTheSameSelectionAndComponentBehavior(InterfaceSource source)
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, source, 0)).Status, Is.EqualTo(ActionStatus.Applied));
            Assert.That(app.Dispatch(new OpenComponentAction(ComponentKind.OrderBook, source, 1)).Status, Is.EqualTo(ActionStatus.Applied));
            Assert.That(app.State.SelectedInstrument, Is.EqualTo(SyntheticCatalog.SkHynix));
            Assert.That(app.State.SecondaryComponent, Is.EqualTo(ComponentKind.OrderBook));
            Assert.That(app.State.CurrentMode, Is.EqualTo(ApplicationMode.Focus));
        }

        [Test]
        public void ADelayedIntentCannotTargetANewerSelection()
        {
            var app = NewDispatcher();
            var delayed = new OpenComponentAction(ComponentKind.Position, InterfaceSource.Gemini, 0);
            app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Hand, 0));
            Assert.That(app.Dispatch(delayed).Reason, Is.EqualTo("CONTEXT_CHANGED"));
            Assert.That(app.State.SecondaryComponent, Is.Null);
        }

        [Test]
        public void ReplayingAnAppliedActionDoesNotMutateStateOrNotifyTwice()
        {
            var app = NewDispatcher();
            int renders = 0;
            app.Changed += _ => renders++;
            var action = new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Hand, 0);
            app.Dispatch(action);
            Assert.That(app.Dispatch(action).Status, Is.EqualTo(ActionStatus.Duplicate));
            Assert.That(app.State.Revision, Is.EqualTo(1));
            Assert.That(renders, Is.EqualTo(1));
        }

        [Test]
        public void ReusedIdWithAlteredInstrumentIsRejected()
        {
            var app = NewDispatcher();
            var id = Guid.NewGuid();
            app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Hand, 0, id));
            var changed = new InstrumentRef(SyntheticCatalog.SkHynix.Id, "005930", "삼성전자");
            Assert.That(app.Dispatch(new SelectInstrumentAction(changed, InterfaceSource.Hand, 0, id)).Reason,
                Is.EqualTo("ACTION_ID_CONFLICT"));
            Assert.That(app.State.SelectedInstrument, Is.EqualTo(SyntheticCatalog.SkHynix));
        }

        [Test]
        public void ReusedIdWithAlteredSourceIsRejected()
        {
            var app = NewDispatcher();
            var id = Guid.NewGuid();
            app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Hand, 0, id));
            Assert.That(app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Gemini, 0, id)).Reason,
                Is.EqualTo("ACTION_ID_CONFLICT"));
        }

        [Test]
        public void EmptyActionIdIsRejected()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Hand, 0, Guid.Empty)).Reason,
                Is.EqualTo("INVALID_ACTION_ID"));
            Assert.That(app.State.Revision, Is.Zero);
        }

        [Test]
        public void MissingInstrumentIsUnavailableAndDoesNotChangeSelection()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new SelectInstrumentAction(null, InterfaceSource.UI, 0)).Reason, Is.EqualTo("INSTRUMENT_UNAVAILABLE"));
            Assert.That(app.State.SelectedInstrument, Is.EqualTo(SyntheticCatalog.Samsung));
        }

        [Test]
        public void UnknownInstrumentIsNotSilentlyReplaced()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new SelectInstrumentAction(new InstrumentRef("unknown", "123456", "Unknown"), InterfaceSource.UI, 0)).Status,
                Is.EqualTo(ActionStatus.Rejected));
            Assert.That(app.State.Revision, Is.Zero);
        }

        [Test]
        public void MismatchedCodeUnderAKnownIdIsRejected()
        {
            var app = NewDispatcher();
            var instrument = new InstrumentRef(SyntheticCatalog.Samsung.Id, "000660", SyntheticCatalog.Samsung.DisplayName);
            Assert.That(app.Dispatch(new SelectInstrumentAction(instrument, InterfaceSource.UI, 0)).Status, Is.EqualTo(ActionStatus.Rejected));
        }

        [Test]
        public void InvalidInitialCatalogIdentityIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new ShellActionDispatcher(
                new ShellState(new InstrumentRef(SyntheticCatalog.Samsung.Id, "000660", "Wrong")), SyntheticCatalog.Instruments));
        }

        [Test]
        public void InvalidSourceIsRejected()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new OpenComponentAction(ComponentKind.Chart, (InterfaceSource)999, 0)).Reason, Is.EqualTo("INVALID_SOURCE"));
        }

        [Test]
        public void CompareRequiresATypedInstrumentPayload()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new OpenComponentAction(ComponentKind.Compare, InterfaceSource.UI, 0)).Reason, Is.EqualTo("UNSUPPORTED_ACTION"));
        }

        [Test]
        public void ComparingWithCurrentSelectionIsRejected()
        {
            var app = NewDispatcher();
            Assert.That(app.Dispatch(new CompareInstrumentAction(SyntheticCatalog.Samsung, InterfaceSource.Hand, 0)).Reason, Is.EqualTo("SAME_INSTRUMENT"));
            Assert.That(app.State.ComparisonInstrument, Is.Null);
        }

        [Test]
        public void SelectingTheComparisonInstrumentRemovesTheNowInvalidComparison()
        {
            var app = NewDispatcher();
            app.Dispatch(new CompareInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Controller, 0));
            app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.Controller, 1));
            Assert.That(app.State.ComparisonInstrument, Is.Null);
            Assert.That(app.State.SecondaryComponent, Is.Null);
        }

        [Test]
        public void FocusModeKeepsAtMostOneSecondarySurface()
        {
            var app = NewDispatcher();
            app.Dispatch(new OpenComponentAction(ComponentKind.OrderBook, InterfaceSource.UI, 0));
            app.Dispatch(new OpenComponentAction(ComponentKind.Position, InterfaceSource.UI, 1));
            Assert.That(app.State.SecondaryComponent, Is.EqualTo(ComponentKind.Position));
            app.Dispatch(new OpenComponentAction(ComponentKind.Chart, InterfaceSource.UI, 2));
            Assert.That(app.State.SecondaryComponent, Is.Null);
            Assert.That(app.State.SelectedInstrument, Is.EqualTo(SyntheticCatalog.Samsung));
        }

        [Test]
        public void ClosingSecondaryKeepsThePrimaryInstrument()
        {
            var app = NewDispatcher();
            app.Dispatch(new OpenComponentAction(ComponentKind.OrderBook, InterfaceSource.Hand, 0));
            app.Dispatch(new CloseSecondaryAction(InterfaceSource.Hand, 1));
            Assert.That(app.State.SecondaryComponent, Is.Null);
            Assert.That(app.State.SelectedInstrument, Is.EqualTo(SyntheticCatalog.Samsung));
        }

        [Test]
        public void BoundedShellReplayCacheStillRejectsOldContextAfterEviction()
        {
            var app = NewDispatcher();
            var first = new OpenComponentAction(ComponentKind.Chart, InterfaceSource.UI, 0);
            app.Dispatch(first);
            for (int i = 1; i <= 256; i++) app.Dispatch(new OpenComponentAction(ComponentKind.Chart, InterfaceSource.UI, i));
            Assert.That(app.Dispatch(first).Reason, Is.EqualTo("CONTEXT_CHANGED"));
            Assert.That(app.State.Revision, Is.EqualTo(257));
        }

        [Test]
        public void ARejectedActionCanBeCorrectedWithoutConsumingItsId()
        {
            var app = NewDispatcher();
            var id = Guid.NewGuid();
            app.Dispatch(new SelectInstrumentAction(null, InterfaceSource.UI, 0, id));
            Assert.That(app.Dispatch(new SelectInstrumentAction(SyntheticCatalog.SkHynix, InterfaceSource.UI, 0, id)).Status,
                Is.EqualTo(ActionStatus.Applied));
        }
    }
}
