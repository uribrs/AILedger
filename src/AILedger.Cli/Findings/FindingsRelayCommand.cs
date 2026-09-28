using System.Net;
using System.Net.Sockets;

namespace AILedger.Cli.Findings;

// No configuration reader or recorder exists here. These arguments only select a live,
// capability-protected host connection; they cannot construct or rebind its authority.
internal static class FindingsRelayCommand
{
    internal static async Task<int> RunAsync(string portText, string secretText, CancellationToken cancellationToken)
    {
        try
        {
            if (!int.TryParse(portText, out var port) || port is < 1 or > 65535 || secretText.Length != 64)
                throw new ArgumentException("Invalid relay address.");
            var secret = Convert.FromHexString(secretText);
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            await stream.WriteAsync(secret, timeout.Token).ConfigureAwait(false);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var input = ForwardInputAsync(Console.OpenStandardInput(), stream, client, connection.Token);
            var output = stream.CopyToAsync(Console.OpenStandardOutput(), connection.Token);
            // EOF on input half-closes the socket; let accepted calls drain their responses.
            var finished = await Task.WhenAny(input, output).ConfigureAwait(false);
            if (finished == output || input.IsFaulted || input.IsCanceled)
                await connection.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(input, output).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException
                                           or ArgumentException or FormatException)
        {
            await Console.Error.WriteLineAsync("Findings relay unavailable; no commit outcome observed.").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task ForwardInputAsync(Stream input, Stream output, TcpClient client, CancellationToken token)
    {
        await input.CopyToAsync(output, token).ConfigureAwait(false);
        client.Client.Shutdown(SocketShutdown.Send);
    }
}
