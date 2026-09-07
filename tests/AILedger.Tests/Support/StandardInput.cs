namespace AILedger.Tests.Support;

// 'artifact record --body-stdin' reads the document from standard input, because the parser takes
// any option value starting with two dashes as the next option name and a real contract opens with
// a horizontal rule or YAML front matter. A test supplies that input the only way a process can:
// by replacing the console reader for the duration of the call, and putting it back afterwards.
internal sealed class StandardInput : IDisposable
{
    // Console.In is process-wide and xunit runs test classes in parallel, so every class that
    // replaces it joins this collection and they run one at a time instead of racing.
    public const string Collection = "standard-input";

    private readonly TextReader _previous;

    public StandardInput(string body)
    {
        _previous = Console.In;
        Console.SetIn(new StringReader(body));
    }

    public void Dispose() => Console.SetIn(_previous);
}

[CollectionDefinition(StandardInput.Collection)]
public sealed class StandardInputCollection
{
}
