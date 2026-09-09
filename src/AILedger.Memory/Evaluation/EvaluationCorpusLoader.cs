using System.Text.Json;
using System.Text.Json.Serialization;
using AILedger.Memory.Contracts;

namespace AILedger.Memory.Evaluation;

public sealed record EvaluationCorpus
{
    public required int SchemaVersion { get; init; }
    public required string CorpusVersion { get; init; }
    public required IReadOnlyList<EvaluationCase> Cases { get; init; }
}

public static class EvaluationCorpusLoader
{
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<EvaluationCorpus> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var corpus = await JsonSerializer.DeserializeAsync<EvaluationCorpus>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The evaluation corpus is empty.");
        Validate(corpus);
        return corpus;
    }

    public static void Validate(EvaluationCorpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (corpus.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Evaluation schema version '{corpus.SchemaVersion}' is not supported; expected version {SupportedSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(corpus.CorpusVersion))
        {
            throw new InvalidDataException("The evaluation corpus version is required.");
        }

        ArgumentNullException.ThrowIfNull(corpus.Cases);
        if (corpus.Cases.Count == 0)
        {
            throw new InvalidDataException("The evaluation corpus must contain at least one case.");
        }

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evaluationCase in corpus.Cases)
        {
            if (evaluationCase is null)
            {
                throw new InvalidDataException("Evaluation cases cannot contain null values.");
            }

            if (string.IsNullOrWhiteSpace(evaluationCase.Id) ||
                string.IsNullOrWhiteSpace(evaluationCase.Query))
            {
                throw new InvalidDataException("Every evaluation case requires a non-empty id and query.");
            }

            if (!caseIds.Add(evaluationCase.Id))
            {
                throw new InvalidDataException($"Evaluation case id '{evaluationCase.Id}' is duplicated.");
            }

            ArgumentNullException.ThrowIfNull(evaluationCase.ExpectedDocumentIds);
            ArgumentNullException.ThrowIfNull(evaluationCase.ExpectedCitationRecordIds);
            ArgumentNullException.ThrowIfNull(evaluationCase.OpeningTags);
            ArgumentNullException.ThrowIfNull(evaluationCase.Kinds);
            if (evaluationCase.ExpectedDocumentIds.Any(string.IsNullOrWhiteSpace) ||
                evaluationCase.ExpectedDocumentIds.Distinct(StringComparer.Ordinal).Count() !=
                evaluationCase.ExpectedDocumentIds.Count)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' has blank or duplicate expected document ids.");
            }

            if (evaluationCase.ExpectedCitationRecordIds.Any(string.IsNullOrWhiteSpace) ||
                evaluationCase.ExpectedCitationRecordIds.Distinct(StringComparer.Ordinal).Count() !=
                evaluationCase.ExpectedCitationRecordIds.Count)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' has blank or duplicate expected citation record ids.");
            }

            if (evaluationCase.OpeningTags.Any(string.IsNullOrWhiteSpace) ||
                evaluationCase.OpeningTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                evaluationCase.OpeningTags.Count)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' has blank or duplicate opening tags.");
            }

            if (evaluationCase.Kinds.Any(kind => !Enum.IsDefined(kind)) ||
                evaluationCase.Kinds.Distinct().Count() != evaluationCase.Kinds.Count)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' has unknown or duplicate document kinds.");
            }

            if (evaluationCase.ExpectNoSupportedAnswer && evaluationCase.ExpectedDocumentIds.Count != 0)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' cannot expect both documents and no supported answer.");
            }

            if (!evaluationCase.ExpectNoSupportedAnswer && evaluationCase.ExpectedDocumentIds.Count == 0)
            {
                throw new InvalidDataException(
                    $"Evaluation case '{evaluationCase.Id}' requires at least one expected document id.");
            }
        }
    }
}
