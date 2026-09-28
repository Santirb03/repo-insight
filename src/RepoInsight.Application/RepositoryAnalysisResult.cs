using RepoInsight.Domain;

namespace RepoInsight.Application;

public sealed record RepositoryAnalysisResult(
    TechnologyProfile Technologies,
    ArchitectureGraph Architecture,
    IReadOnlyList<DiagnosticFinding> Diagnostics,
    RepositoryNarrative Narrative,
    string Mermaid);