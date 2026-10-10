using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.TaskClosure;

namespace AILedger.Cli.Tasks;

internal interface IHumanClosureConfirmation
{
    Task<HumanClosureAdmission> ConfirmAsync(GovernedTaskState state, ActorId actor, string reason,
        string ledgerRoot, CancellationToken token);
}

internal sealed class MacHumanClosureConfirmation : IHumanClosureConfirmation
{
    public async Task<HumanClosureAdmission> ConfirmAsync(GovernedTaskState state, ActorId actor,
        string reason, string ledgerRoot, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS())
            throw new GovernanceException("Human-only closure currently requires the macOS local authentication host. No terminal or unattended fallback is permitted.");
        var helper = Path.Combine(AppContext.BaseDirectory, "ailedger-human-confirm");
        if (!File.Exists(helper))
            throw new GovernanceException("The installed human confirmation helper is missing. Reinstall the complete kernel package.");
        var body = JsonSerializer.Serialize(new
        {
            task = state.TaskId.Value, title = state.Title, actor = actor.Value, reason,
            ledgerRoot = Path.GetFullPath(ledgerRoot), version = state.Version,
            activeRuns = state.Runs.Values.Count(run => run.Status == AgentRunStatus.Active),
            unfinishedWork = state.WorkItems.Values.Count(work => work.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned))
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = new Process { StartInfo = new ProcessStartInfo(helper)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        try
        {
            if (!process.Start()) throw new GovernanceException("Unable to start human confirmation.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new GovernanceException("Unable to start the installed human confirmation helper: " + error.Message);
        }
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteAsync(body.AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var response = await output.ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new GovernanceException("Human confirmation was declined or OS authentication was unavailable. The task remains unchanged.");
            var receipt = JsonSerializer.Deserialize<AuthenticationResponse>(response);
            if (receipt is null || receipt.RequestSha256 != hash || string.IsNullOrWhiteSpace(receipt.Identity))
                throw new GovernanceException("Human confirmation did not match this closure request.");
            return new(state.TaskId, actor, state.Version, reason, DateTimeOffset.UtcNow,
                "macos-local-authentication", receipt.Identity, state.Roles[actor].AssignedBy);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new GovernanceException("Human confirmation timed out; the task remains unchanged.");
        }
        catch (JsonException)
        {
            throw new GovernanceException("The human confirmation host returned an invalid receipt; the task remains unchanged.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private sealed record AuthenticationResponse(string RequestSha256, string Identity);
}
