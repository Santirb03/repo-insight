using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface IArchitectureDiagnosticRule
{
    IReadOnlyList<DiagnosticFinding> Evaluate(
        ArchitectureGraph graph);
}