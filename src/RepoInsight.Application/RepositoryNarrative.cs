namespace RepoInsight.Application;

public sealed record RepositoryNarrative(
    string Summary,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Recommendations);