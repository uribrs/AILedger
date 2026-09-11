using System.Runtime.CompilerServices;

// HistoricalLedgerProjector is internal because nothing outside this assembly should replay the log
// through it — LedgerDocumentNormalizer is the way in. But it is one of the kernel's three readers of
// the same events, and the test that matters most about it is that its projection agrees with the
// canonical reducer's, field for field. That test has to hold the projector itself: comparing two
// rebuilt documents can only see the fields a document happens to render, and the ten fields this
// reader had silently fallen behind on were mostly fields no document renders (VC3, VE7).
[assembly: InternalsVisibleTo("AILedger.Memory.Tests")]
