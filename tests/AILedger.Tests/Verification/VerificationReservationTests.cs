using AILedger.Cli.Verification;
using AILedger.Core.Domain;
using AILedger.Tests.Support;

namespace AILedger.Tests.Verification;

// R6 (result-binds-measured-bytes), m4 (PC45), contract S11. On v2 the id was a check followed by an
// idempotent create, so two runs could share a directory and a failure before the process burned the
// id. No test-only hook injects a pre-spawn failure into the command (SC8); the release is proven here
// on the type the command uses.
public sealed class VerificationReservationTests
{
    [Fact]
    public void R6_SecondTakeIsRefused()
    {
        using var root = new TemporaryDirectory();
        var directory = Path.Combine(root.Path, "verification", "PV1");
        using var first = VerificationReservation.Take(directory);

        var refusal = Assert.Throws<GovernanceException>(() => VerificationReservation.Take(directory));

        Assert.Equal(
            "verification run: evidence id 'PV1' is already reserved by another verification run",
            refusal.Message);
        // The refused attempt did not release the holder's reservation.
        Assert.True(File.Exists(Path.Combine(directory, ".reservation")));
    }

    [Fact]
    public void R6_UncommittedReservationIsReleased()
    {
        using var root = new TemporaryDirectory();
        var directory = Path.Combine(root.Path, "verification", "PV1");
        var reservation = VerificationReservation.Take(directory);
        File.WriteAllText(Path.Combine(directory, "stdout.log"), "partial");

        reservation.Dispose();

        Assert.False(Directory.Exists(directory));
        using var again = VerificationReservation.Take(directory);
        Assert.True(File.Exists(Path.Combine(directory, ".reservation")));
    }

    [Fact]
    public void R6_CommittedReservationIsKept()
    {
        using var root = new TemporaryDirectory();
        var directory = Path.Combine(root.Path, "verification", "PV1");
        var reservation = VerificationReservation.Take(directory);

        reservation.Commit();
        reservation.Dispose();

        var marker = new FileInfo(Path.Combine(directory, ".reservation"));
        Assert.True(marker.Exists);
        Assert.Equal(0, marker.Length);
        Assert.Throws<GovernanceException>(() => VerificationReservation.Take(directory));
    }
}
