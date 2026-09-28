using System.Runtime.CompilerServices;

namespace AILedger.Tests.Support;

internal static class TestEnvironment
{
    // CLI fixtures that omit --cognitive-root must read the same layer they were briefed from.
    // Configure it once before parallel tests start, using the production-supported setting.
    // This affects only this test process and its children, never the shell or installed tool.
    [ModuleInitializer]
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("AILEDGER_COGNITIVE_ROOT", ContextBrief.CognitiveRoot());
}
