using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using AILedger.Cli.Verification;
using AILedger.Tests.Support;

namespace AILedger.Tests.Verification;

public sealed class ContainerCleanupTests
{
    // R2 (label-only-cleanup). The engine holds containers of other live work beside this run's. The
    // list must ask for exactly this run's label, and the only deletes sent must be for the ids that
    // list returned. Anything else would remove another task's container (K2), so the request log is
    // compared whole: no prune, no other delete, no other call.
    [Fact]
    public async Task R2_DeletesOnlyIdsReturnedForTheExactLabel()
    {
        var engine = new FakeDockerEngine();
        engine.Owned.AddRange(["mine-1", "mine-2"]);

        var record = await ContainerCleanup.RunAsync(engine.Create("/unused"), "T1.PV1", CancellationToken.None);

        Assert.Equal("ailedger.run=T1.PV1", record.Label);
        Assert.Equal(new[] { "mine-1", "mine-2" }, record.Removed);
        Assert.Empty(record.Failed);
        var requests = engine.Requests;
        Assert.Equal(3, requests.Count);
        var list = requests[0];
        Assert.Equal(HttpMethod.Get, list.Method);
        Assert.StartsWith("/containers/json?all=true&filters=", list.PathAndQuery, StringComparison.Ordinal);
        var filter = Uri.UnescapeDataString(list.PathAndQuery["/containers/json?all=true&filters=".Length..]);
        using (var document = JsonDocument.Parse(filter))
        {
            var property = Assert.Single(document.RootElement.EnumerateObject());
            Assert.Equal("label", property.Name);
            Assert.Equal("ailedger.run=T1.PV1", Assert.Single(property.Value.EnumerateArray()).GetString());
        }

        Assert.Equal((HttpMethod.Delete, "/containers/mine-1?force=true&v=true"), requests[1]);
        Assert.Equal((HttpMethod.Delete, "/containers/mine-2?force=true&v=true"), requests[2]);
    }

    // R2 control: a label that matches nothing deletes nothing, even with containers on the engine
    // that belong to someone else. The fake answers only the owned list, so an empty list is the case.
    [Fact]
    public async Task R2_AnEmptyLabelListDeletesNothing()
    {
        var engine = new FakeDockerEngine();

        var record = await ContainerCleanup.RunAsync(engine.Create("/unused"), "T1.PV1", CancellationToken.None);

        Assert.Empty(record.Removed);
        Assert.Empty(record.Failed);
        Assert.Equal(HttpMethod.Get, Assert.Single(engine.Requests).Method);
    }

    // S8: a failed delete is listed and the others still run; a failed list is recorded with a null
    // id. Neither throws, because the evidence entry is written after cleanup returns.
    [Fact]
    public async Task CleanupFailuresAreRecordedAndNotFatal()
    {
        var engine = new FakeDockerEngine();
        engine.Owned.AddRange(["mine-1", "mine-2"]);
        engine.FailingDeletes.Add("mine-1");

        var record = await ContainerCleanup.RunAsync(engine.Create("/unused"), "T1.PV1", CancellationToken.None);

        Assert.Equal(new[] { "mine-2" }, record.Removed);
        var failure = Assert.Single(record.Failed);
        Assert.Equal("mine-1", failure.Id);
        Assert.Contains("500", failure.Error, StringComparison.Ordinal);

        var broken = new FakeDockerEngine
        {
            Override = _ => FakeDockerEngine.Text(HttpStatusCode.InternalServerError, "engine down")
        };
        var listFailed = await ContainerCleanup.RunAsync(broken.Create("/unused"), "T1.PV1", CancellationToken.None);

        Assert.Empty(listFailed.Removed);
        Assert.Null(Assert.Single(listFailed.Failed).Id);
        Assert.Equal(HttpMethod.Get, Assert.Single(broken.Requests).Method);
    }

    // S7: each cause has its own message, and the check passes only on OK. The socket file is a plain
    // file in a temporary directory; nothing is bound (PALT10).
    [Theory]
    [InlineData("ok", null)]
    [InlineData("missing", "Colima is not started, or DOCKER_HOST names another path")]
    [InlineData("denied", "this process cannot reach the socket; it is probably sandboxed; run from a host shell")]
    [InlineData("refused", "Colima is stopped or not responding")]
    [InlineData("not-ok", "Colima is stopped or not responding")]
    [InlineData("tcp", "is not a unix:// socket")]
    public async Task SocketPreflightNamesTheCause(string state, string? expected)
    {
        using var directory = new TemporaryDirectory();
        var socket = Path.Combine(directory.Path, "docker.sock");
        if (state != "missing")
        {
            File.WriteAllText(socket, string.Empty);
        }

        var engine = new FakeDockerEngine
        {
            Override = state switch
            {
                "denied" => _ => throw new HttpRequestException("x", new SocketException((int)SocketError.AccessDenied)),
                "refused" => _ => throw new HttpRequestException("x", new SocketException((int)SocketError.ConnectionRefused)),
                "not-ok" => _ => FakeDockerEngine.Text(HttpStatusCode.OK, "maybe"),
                _ => null
            }
        };
        var dockerHost = state == "tcp" ? "tcp://127.0.0.1:2375" : $"unix://{socket}";

        var cause = await DockerSocketPreflight.CheckAsync(dockerHost, engine.Create, CancellationToken.None);

        if (expected is null)
        {
            Assert.Null(cause);
            Assert.Equal((HttpMethod.Get, "/_ping"), Assert.Single(engine.Requests));
            Assert.Equal(new[] { socket }, engine.SocketPaths);
        }
        else
        {
            Assert.NotNull(cause);
            Assert.Contains(expected, cause, StringComparison.Ordinal);
        }

        if (state is "missing" or "tcp")
        {
            Assert.Empty(engine.SocketPaths);
        }

        // S7 never deletes and never asks anything but the ping.
        Assert.All(engine.Requests, request => Assert.Equal((HttpMethod.Get, "/_ping"), request));
    }
}
