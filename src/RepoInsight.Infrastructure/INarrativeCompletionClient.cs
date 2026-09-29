namespace RepoInsight.Infrastructure;

public sealed record NarrativePrompt(string Instructions, string UntrustedData);

public interface INarrativeCompletionClient
{
    Task<string> CompleteAsync(NarrativePrompt prompt, CancellationToken cancellationToken);
}
