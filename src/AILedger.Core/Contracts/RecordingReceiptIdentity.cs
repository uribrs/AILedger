namespace AILedger.Core.Contracts;

// Only the identity needed to join observations to storage-validated canonical receipts.
public interface IRecordingReceiptIdentity
{
    string TaskId { get; }
    string ActorId { get; }
    string? RunId { get; }
    string RequestId { get; }
    string TransactionId { get; }
    string PayloadFingerprint { get; }
    string CorrelationId { get; }
    string? CausationId { get; }
    IReadOnlyList<string> EventIds { get; }
}
