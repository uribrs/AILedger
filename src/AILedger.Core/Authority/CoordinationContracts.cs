using AILedger.Core.Contracts;
namespace AILedger.Core.Authority;

public interface IDurableHostRequests { }

// Execution ownership is separate from task judgment and acceptance.
public sealed record CoordinationLease(TaskId TaskId, string Owner, long Epoch);
public sealed record HostRequestIdentity(int SchemaVersion, string Binding, string RequestId, string Body);
