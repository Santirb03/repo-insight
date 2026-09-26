namespace RepoInsight.Domain;

public sealed record DetectedTechnology(
    string Name, TechnologyCategory Category, TechnologyConfidence Confidence, IReadOnlyList<string> Evidence);
