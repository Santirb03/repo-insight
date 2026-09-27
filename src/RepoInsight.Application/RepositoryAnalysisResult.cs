using RepoInsight.Domain;

namespace RepoInsight.Application;

public sealed record RepositoryAnalysisResult(
    TechnologyProfile Technologies,
    ArchitectureGraph Architecture,
    string Mermaid);