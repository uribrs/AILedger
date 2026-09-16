using AILedger.Cli;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using System.Reflection;

namespace AILedger.Tests.Cli;

public sealed class CliCommandCatalogTests
{
    [Fact]
    public void DuplicateCommandNamesAreRejected()
    {
        var first = Registration("status");
        var duplicate = Registration("STATUS");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CliCommandCatalog([first, duplicate]));

        Assert.Contains("status", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationCatalogContainsEverySupportedCommandAndAlias()
    {
        var application = Application();

        Assert.Equal(
            new[]
            {
                "actor attach", "alternative record", "artifact list", "artifact record", "artifact show",
                "audit", "batch preflight", "challenge dispose", "challenge raise", "claim add", "claim resolve",
                "constraint add", "constraint supersede", "context build", "decision propose",
                "decision resolve", "escalation raise", "escalation resolve", "evidence add", "history",
                "lesson mark", "lesson recheck", "preflight batch", "provider launch", "provider resume", "retrospective build",
                "retrospective record", "run complete", "run start", "session complete", "session start",
                "stage transition", "status", "task history", "task open", "task status", "version", "who",
                "work abandon", "work add", "work block", "work complete", "work unblock"
            },
            application.CommandCatalog.Names.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AliasesResolveToOneRegistrationWithItsOptionsAndReadClassification()
    {
        var application = Application();

        Assert.True(application.CommandCatalog.TryGet("status", out var status));
        Assert.True(application.CommandCatalog.TryGet("task status", out var taskStatus));
        Assert.Same(status, taskStatus);
        Assert.True(status.IsReadOnly);
        Assert.Contains("task", status.AllowedOptions);

        Assert.True(application.CommandCatalog.TryGet("claim add", out var claimAdd));
        Assert.False(claimAdd.IsReadOnly);
        Assert.Contains("statement", claimAdd.AllowedOptions);
    }

    [Fact]
    public void PublicApplicationFacadeRemainsStable()
    {
        var type = typeof(CliApplication);
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var declaredMethods = type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public |
                                              BindingFlags.Instance | BindingFlags.Static);

        Assert.Equal(2, constructors.Length);
        Assert.Contains(declaredMethods, method => method.Name == nameof(CliApplication.CreateDefault));
        Assert.Contains(declaredMethods, method => method.Name == nameof(CliApplication.RunAsync));
        Assert.NotNull(type.GetProperty(nameof(CliApplication.HarnessTranscriptRoot)));
    }

    private static CliApplication Application() => new(
        TextWriter.Null,
        TextWriter.Null,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

    private static CliCommandRegistration Registration(string name) =>
        new([name], [], isReadOnly: true, (_, _) => Task.CompletedTask);

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
