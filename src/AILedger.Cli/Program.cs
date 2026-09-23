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
    if (args is ["navigation", "serve", var configurationPath])
    {
        return await RoslynNavigationServer.RunAsync(configurationPath, cancellation.Token);
    }

    return await CliApplication.CreateDefault().RunAsync(args, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
