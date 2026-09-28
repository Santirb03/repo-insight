using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IRepositoryDiagnosticRule
{
    IReadOnlyList<DiagnosticFinding> Evaluate(
        RepositoryScan scan,
        ArchitectureGraph graph);
}