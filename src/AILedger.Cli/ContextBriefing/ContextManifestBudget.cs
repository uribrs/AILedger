using System.Globalization;
using System.Text;
using System.Text.Json;
using AILedger.Core.Contracts;

namespace AILedger.Cli.ContextBriefing;

// Bound the exact JSON delivered by the CLI, including its trailing newline. Provider launches
// use the same serializer without that newline, so this is conservative for that path.
internal static class ContextManifestBudget
{
    internal const int DefaultMaximumBytes = 256 * 1024;
    private const string OmissionNotice =
        "Background lessons or lesson marks were omitted to fit the byte limit. Required task records " +
        "and referenced lessons are intact. Omission does not mean no prior lessons exist. " +
        "Use context build with the same actor/work selection and a larger --max-context-bytes to retrieve them.";

    internal static int ReadMaximumBytes(CommandLine input)
    {
        var value = input.Optional("max-context-bytes");
        if (value is null) return DefaultMaximumBytes;
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) && maximum > 0)
            return maximum;
        throw new CliUsageException("--max-context-bytes must be a positive integer.");
    }

    internal static ContextManifest Apply(ContextManifest manifest, int maximumBytes, JsonSerializerOptions json)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var referenced = manifest.Artifacts
            .Where(item => item.Kind is not (ContextArtifactKind.Lesson or ContextArtifactKind.LessonMark))
            .SelectMany(item => item.RelatedIds).ToHashSet(StringComparer.Ordinal);
        var background = manifest.Artifacts
            .Where(item => item.Kind is ContextArtifactKind.Lesson or ContextArtifactKind.LessonMark)
            .Where(item => !referenced.Contains(item.Id)).ToArray();

        ContextManifest Keep(int count)
        {
            var omitted = background.Skip(count).Select(item => (item.Kind, item.Id)).ToHashSet();
            return manifest with
            {
                Artifacts = manifest.Artifacts.Where(item => !omitted.Contains((item.Kind, item.Id))).ToArray(),
                Budget = new ContextBudgetReport(maximumBytes, background.Length - count,
                    count == background.Length ? null : OmissionNotice)
            };
        }

        int Bytes(ContextManifest value) =>
            JsonSerializer.SerializeToUtf8Bytes(value, json).Length + Encoding.UTF8.GetByteCount(Environment.NewLine);

        var full = Keep(background.Length);
        if (Bytes(full) <= maximumBytes) return full;
        var required = Keep(0);
        var requiredBytes = Bytes(required);
        if (requiredBytes > maximumBytes)
            throw new CliUsageException(
                $"Required context is {requiredBytes} UTF-8 bytes; --max-context-bytes is {maximumBytes}. " +
                "No required records were truncated. Select a narrower --work brief, reduce its required inputs, " +
                "or explicitly increase --max-context-bytes.");

        var low = 0;
        var high = background.Length - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (Bytes(Keep(middle)) <= maximumBytes) low = middle;
            else high = middle - 1;
        }

        return Keep(low);
    }
}
