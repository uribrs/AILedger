using System.Text;
using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Core.Handoffs;
using AILedger.Core.Inspection;
using AILedger.Core.Contracts;

namespace AILedger.Cli.Handoffs;

internal sealed class HandoffCliCommands(CliCommandExecutor executor, TextReader input)
{
    internal IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new(["handoff index"], CliCommandOptions.Set("root", "task", "actor", "run", "selection"), true, IndexAsync);
        yield return new(["handoff retrieve"], CliCommandOptions.Set("root", "task", "actor", "run", "body-stdin"), true, RetrieveAsync);
        yield return new(["handoff prepare"], CliCommandOptions.Set("root", "task", "actor", "run", "body-stdin"), true, PrepareAsync);
    }

    private async Task IndexAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        var result = await Preparer(invocation).IndexAsync(Binding(invocation), Path.GetFullPath(invocation.LedgerRoot),
            invocation.Input.Optional("selection") ?? "task", null, token).ConfigureAwait(false);
        await executor.WriteLineAsync(JsonSerializer.Serialize(result, HandoffJson.Options)).ConfigureAwait(false);
    }

    private async Task RetrieveAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (!invocation.Input.Flag("body-stdin")) throw new CliUsageException("handoff retrieve requires --body-stdin.");
        var query = HandoffJson.ParseRetrieval(await ReadAsync(token).ConfigureAwait(false));
        var inspector = invocation.Service as ITaskInspector ?? throw new CliUsageException("Task inspection unavailable.");
        var result = await inspector.RetrieveAsync(Binding(invocation), query, token).ConfigureAwait(false);
        await executor.WriteLineAsync(JsonSerializer.Serialize(result, HandoffJson.Options)).ConfigureAwait(false);
    }

    private async Task PrepareAsync(CliCommandInvocation invocation, CancellationToken token)
    {
        if (!invocation.Input.Flag("body-stdin")) throw new CliUsageException("handoff prepare requires --body-stdin.");
        var request = HandoffJson.Parse(await ReadAsync(token).ConfigureAwait(false));
        if (request.LedgerIdentity != Path.GetFullPath(invocation.LedgerRoot))
            throw new CliUsageException("Ledger identity differs from the indexed root; re-index this ledger.");
        var result = await Preparer(invocation).PrepareAsync(Binding(invocation), request, token).ConfigureAwait(false);
        await executor.WriteLineAsync(JsonSerializer.Serialize(result, HandoffJson.Options)).ConfigureAwait(false);
    }

    private async Task<string> ReadAsync(CancellationToken token)
    {
        var body = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            body.Append(buffer, 0, read);
            if (body.Length > HandoffJson.MaximumRequestBytes) throw new CliUsageException("Handoff body exceeds 512 KiB.");
        }
        return body.ToString();
    }
    private static HandoffPreparer Preparer(CliCommandInvocation invocation) =>
        new(invocation.Service as ITaskInspector ?? throw new CliUsageException("Task inspection unavailable."));
    private static InspectionBinding Binding(CliCommandInvocation invocation)
    {
        var run = invocation.Input.Optional("run");
        // Like other operator CLI reads, the explicit principal is selected outside the authored body.
        // Existing BuildContext and active-run/role isolation are checked by ITaskInspector on each read.
        return new(CliInput.Task(invocation.Input), CliInput.Actor(invocation.Input), run is null ? null : new RunId(run),
            run ?? "handoff-preparation", AllowInspect: true, AllowRunless: run is null);
    }
}
