using AILedger.Core.Domain;

namespace AILedger.Cli.Verification;

/// <summary>
/// Contract S11: the exclusive claim on one evidence id's result directory. The claim is an empty
/// <c>.reservation</c> file created with <see cref="FileMode.CreateNew"/>, so two verification runs
/// with the same id cannot both hold it. Until <see cref="Commit"/> the directory belongs to nothing
/// that was run, and disposing releases it so the id can be used again. From <see cref="Commit"/>,
/// called just before the spawner, the directory is the run's record and is kept.
/// </summary>
internal sealed class VerificationReservation : IDisposable
{
    public const string FileName = ".reservation";

    private readonly string _directory;
    private bool _committed;
    private bool _disposed;

    private VerificationReservation(string directory) => _directory = directory;

    public static VerificationReservation Take(string directory)
    {
        Directory.CreateDirectory(directory);
        var marker = Path.Combine(directory, FileName);
        try
        {
            new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None).Dispose();
        }
        catch (IOException) when (File.Exists(marker))
        {
            throw new GovernanceException(
                $"verification run: evidence id '{Path.GetFileName(directory)}' is already reserved by another verification run");
        }

        return new VerificationReservation(directory);
    }

    public void Commit() => _committed = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_committed)
        {
            return;
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Release is best effort: it runs while another exception is already propagating, and
            // must not replace it. A directory left behind is refused as used, never reused.
        }
    }
}
