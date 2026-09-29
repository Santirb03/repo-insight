using System.Text.Json;
using RepoInsight.Application;
using RepoInsight.Domain;

namespace RepoInsight.Infrastructure;

public sealed class AzureOpenAIRepositoryAnalysisNarrator(
    INarrativeCompletionClient client,
    DeterministicRepositoryAnalysisNarrator fallback,
    AzureOpenAINarratorOptions options) : IRepositoryAnalysisNarrator
{
    public async Task<RepositoryNarrative> GenerateAsync(TechnologyProfile technologies, ArchitectureGraph architecture,
        IReadOnlyList<DiagnosticFinding> diagnostics, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(technologies);
        ArgumentNullException.ThrowIfNull(architecture);
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        if (options.IsValid())
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                var prompt = RepositoryNarrativePromptBuilder.Build(technologies, architecture, diagnostics);
                var json = await client.CompleteAsync(prompt, timeout.Token).WaitAsync(timeout.Token);
                cancellationToken.ThrowIfCancellationRequested();
                var result = Validate(json);
                if (result is not null) return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Optional explanation layer must not break deterministic analysis. Never log the
                // exception, prompt, response or configuration: any of these could contain secrets.
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        return await fallback.GenerateAsync(technologies, architecture, diagnostics, cancellationToken);
    }

    private static RepositoryNarrative? Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 20_000) return null;
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var names = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != 4 || !names.Order(StringComparer.Ordinal).SequenceEqual(new[] { "Recommendations", "Risks", "Strengths", "Summary" })) return null;
        var summary = ReadText(root.GetProperty("Summary"), 2000);
        var strengths = ReadList(root.GetProperty("Strengths"));
        var risks = ReadList(root.GetProperty("Risks"));
        var recommendations = ReadList(root.GetProperty("Recommendations"));
        return summary is null || strengths is null || risks is null || recommendations is null
            ? null : new RepositoryNarrative(summary, strengths, risks, recommendations);
    }

    private static string? ReadText(JsonElement element, int limit)
    {
        if (element.ValueKind != JsonValueKind.String) return null;
        var value = element.GetString();
        return string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t')
            ? null : value.Trim();
    }

    private static IReadOnlyList<string>? ReadList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 8) return null;
        var values = element.EnumerateArray().Select(item => ReadText(item, 1000)).ToArray();
        return values.Any(value => value is null) ? null : Array.AsReadOnly(values.Select(value => value!).ToArray());
    }
}
