using AILedger.Core.Domain;
using AILedger.Providers.Verification;

namespace AILedger.Cli.Verification;

/// <summary>
/// Contract S9: a provider launch whose working directory has a valid profile declaring
/// <c>launchPreflight: true</c> is refused before any cost when the Docker socket cannot be reached.
/// Only a declaration triggers it (PALT8); a repository with no profile, or with profiles that do not
/// declare it, is never checked. A profile file that cannot be read or parsed declares nothing that
/// can be trusted, so it is not checked either and the launch proceeds (PALT16): refusing it would
/// block the governed run dispatched to repair it. The briefing reports the problem, and
/// <c>verification run</c> still refuses the file.
/// </summary>
internal static class VerificationLaunchPreflight
{
    public static async Task EnsureAsync(
        string workingDirectory,
        VerificationHost host,
        CancellationToken cancellationToken)
    {
        VerificationProfileFile? file;
        try
        {
            file = VerificationProfileFile.Find(workingDirectory);
        }
        catch (VerificationProfileException)
        {
            return;
        }

        if (file?.Profiles.FirstOrDefault(profile => profile.LaunchPreflight) is not { } declaring)
        {
            return;
        }

        if (await DockerSocketPreflight.CheckAsync(
                host.DockerHost(), host.CreateEngineHandler, cancellationToken).ConfigureAwait(false) is { } unreachable)
        {
            throw new GovernanceException(
                $"Launch refused by the verification profile preflight: profile '{declaring.Name}' in " +
                $"'{file.Path}' declares launchPreflight, and {unreachable}.");
        }
    }
}
