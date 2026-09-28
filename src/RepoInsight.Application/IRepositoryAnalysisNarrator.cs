using RepoInsight.Domain;

namespace RepoInsight.Application;

public interface IRepositoryAnalysisNarrator
{
    Task<RepositoryNarrative> GenerateAsync(
        TechnologyProfile technologies,
        ArchitectureGraph architecture,
        IReadOnlyList<DiagnosticFinding> diagnostics,
        CancellationToken cancellationToken = default);
}