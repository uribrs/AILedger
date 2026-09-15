namespace AILedger.Core.Contracts;

public sealed record LessonMinted(Lesson Lesson) : LedgerEventData;
