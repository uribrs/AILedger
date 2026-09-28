using System.Runtime.InteropServices;
using AILedger.Cli;
using AILedger.Providers.Navigation;

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

Console.CancelKeyPress += cancelHandler;
using var termination = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        cancellation.Cancel();
    });

try
{
    if (args is ["findings", "relay", var port, var secret])
    {
        return await AILedger.Cli.Findings.FindingsRelayCommand.RunAsync(port, secret, cancellation.Token);
    }

    if (args is ["findings", "serve", var findingsConfigurationPath])
    {
        return await AILedger.Cli.Findings.FindingsStdioCommand.RunAsync(findingsConfigurationPath, cancellation.Token);
    }

    if (args is ["navigation", "serve", var configurationPath])
    {
        return await RoslynNavigationServer.RunAsync(configurationPath, cancellation.Token);
    }

    if (args is ["navigation", "guard", var guardConfigurationPath])
    {
        return await RoslynSearchGuard.RunAsync(guardConfigurationPath, cancellation.Token);
    }

    return await CliApplication.CreateDefault().RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
