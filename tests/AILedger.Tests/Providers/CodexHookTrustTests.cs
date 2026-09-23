using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Providers.Adapters;
using AILedger.Providers.Navigation;
using AILedger.Tests.Support;

namespace AILedger.Tests.Providers;

public sealed class CodexHookTrustTests
{
    private const string Command = "dotnet /generated/host.dll navigation guard /generated/settings.json";
    private static readonly string Hash = "sha256:" + new string('a', 64);

    [Fact]
    public void TrustsOnlyTheExactGeneratedDefinition()
    {
        using var home = new TemporaryDirectory();
        var response = Response(home.Path);
        var text = CodexHookTrust.ValidateResponse(Element(response), home.Path, home.Path, Command);
        Assert.Contains($"[hooks.state.{RoslynNavigation.Quote(Key(home.Path))}]", text);
        Assert.Contains($"trusted_hash = \"{Hash}\"", text);
        Assert.Contains("enabled = true", text);
        Assert.DoesNotContain("bypass", text);
    }

    [Theory]
    [InlineData("command", "unexpected command")]
    [InlineData("source", "project")]
    [InlineData("sourcePath", "/ambient/hooks.json")]
    [InlineData("eventName", "sessionStart")]
    [InlineData("matcher", "^UnrelatedTool$")]
    [InlineData("currentHash", "not-a-hash")]
    [InlineData("key", "/ambient/hooks.json:pre_tool_use:0:0")]
    public void RejectsChangedOrAmbientHook(string property, string value)
    {
        using var home = new TemporaryDirectory();
        var response = Response(home.Path);
        response["result"]!["data"]![0]!["hooks"]![0]![property] = value;
        Assert.Throws<InvalidOperationException>(() =>
            CodexHookTrust.ValidateResponse(Element(response), home.Path, home.Path, Command));
    }

    [Fact]
    public void RejectsExtraHooksAndDiscoveryErrors()
    {
        using var home = new TemporaryDirectory();
        var response = Response(home.Path);
        var entry = response["result"]!["data"]![0]!;
        var hooks = (JsonArray)entry["hooks"]!;
        hooks.Add(hooks[0]!.DeepClone());
        Assert.Throws<InvalidOperationException>(() =>
            CodexHookTrust.ValidateResponse(Element(response), home.Path, home.Path, Command));
        hooks.RemoveAt(1);
        ((JsonArray)entry["errors"]!).Add(new JsonObject { ["message"] = "invalid hooks" });
        Assert.Throws<InvalidOperationException>(() =>
            CodexHookTrust.ValidateResponse(Element(response), home.Path, home.Path, Command));
    }

    [Theory]
    [InlineData("async", true)]
    [InlineData("isManaged", true)]
    [InlineData("enabled", false)]
    public void RequiresAnEnabledSynchronousUserHook(string property, bool value)
    {
        using var home = new TemporaryDirectory();
        var response = Response(home.Path);
        response["result"]!["data"]![0]!["hooks"]![0]![property] = value;
        Assert.Throws<InvalidOperationException>(() =>
            CodexHookTrust.ValidateResponse(Element(response), home.Path, home.Path, Command));
    }

    [Fact]
    public async Task CancellationStopsAHungProbeWithoutWritingTrust()
    {
        if (OperatingSystem.IsWindows()) return;
        using var home = new TemporaryDirectory();
        var executable = Path.Combine(home.Path, "codex-probe");
        var config = Path.Combine(home.Path, "config.toml");
        await File.WriteAllTextAsync(config, "[features]\nhooks = true\n");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nIFS= read -r initialize\nIFS= read -r hold_open\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CodexHookTrust.TrustAsync(executable, home.Path, home.Path, Command, cancellation.Token));

        Assert.DoesNotContain("trusted_hash", await File.ReadAllTextAsync(config));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExchangesProtocolUsingCanonicalPathsAndStopsTheProcess(bool useAlias)
    {
        if (OperatingSystem.IsWindows()) return;
        using var home = new TemporaryDirectory();
        var requestedHome = home.Path;
        if (useAlias)
        {
            var target = Directory.CreateDirectory(Path.Combine(home.Path, "physical")).FullName;
            requestedHome = Path.Combine(home.Path, "alias");
            Directory.CreateSymbolicLink(requestedHome, target);
        }

        var canonicalHome = RoslynSolutionPaths.Canonicalize(requestedHome);
        var executable = Path.Combine(home.Path, "codex-probe");
        await File.WriteAllTextAsync(Path.Combine(requestedHome, "config.toml"), "[features]\nhooks = true\n");
        await File.WriteAllTextAsync(Path.Combine(requestedHome, "response.json"), Response(canonicalHome).ToJsonString() + "\n");
        await File.WriteAllTextAsync(executable, """
            #!/bin/sh
            test "$1" = app-server || exit 12
            IFS= read -r initialize || exit 13
            printf '%s\n' '{"id":1,"result":{}}'
            IFS= read -r initialized || exit 14
            IFS= read -r list || exit 15
            printf '%s' "$CODEX_HOME" > "$CODEX_HOME/observed-home"
            cat "$CODEX_HOME/response.json"
            IFS= read -r hold_open
            """ + "\n");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        await CodexHookTrust.TrustAsync(executable, requestedHome, requestedHome, Command, CancellationToken.None);

        var config = await File.ReadAllTextAsync(Path.Combine(requestedHome, "config.toml"));
        Assert.Contains($"trusted_hash = \"{Hash}\"", config);
        Assert.Contains(RoslynNavigation.Quote(Key(canonicalHome)), config);
        Assert.Equal(canonicalHome, await File.ReadAllTextAsync(Path.Combine(requestedHome, "observed-home")));
    }

    [Fact]
    public void RejectsUnsupportedProtocol()
    {
        using var home = new TemporaryDirectory();
        using var response = JsonDocument.Parse("{\"id\":2,\"error\":{\"message\":\"unknown method\"}}");
        Assert.Throws<InvalidOperationException>(() =>
            CodexHookTrust.ValidateResponse(response.RootElement, home.Path, home.Path, Command));
    }

    private static string Key(string home) => Path.Combine(home, "hooks.json") + ":pre_tool_use:0:0";

    private static JsonElement Element(JsonObject response) => JsonSerializer.SerializeToElement(response);

    private static JsonObject Response(string home) => new()
    {
        ["id"] = 2,
        ["result"] = new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["cwd"] = home, ["errors"] = new JsonArray(),
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["key"] = Key(home), ["currentHash"] = Hash, ["handlerType"] = "command",
                    ["eventName"] = "preToolUse", ["matcher"] = CodexHookTrust.GuardMatcher,
                    ["command"] = Command, ["source"] = "user", ["sourcePath"] = Path.Combine(home, "hooks.json"),
                    ["enabled"] = true, ["isManaged"] = false, ["async"] = false
                })
            })
        }
    };
}
