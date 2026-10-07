using AILedger.Core.Contracts;
namespace AILedger.Cli.Dispatch;

// Private trusted-host continuation. The completion credential is never in a public receipt.
internal interface IDispatchCheckpoint
{
    Task SaveAsync(ProviderDispatchRequest request, ProviderDispatchResult receipt, CompleteRunCommand? completion, CancellationToken token);
}
