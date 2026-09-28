using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ArchitectureDiagnosticsEngine(
    IEnumerable<IArchitectureDiagnosticRule> architectureRules,
    IEnumerable<IRepositoryDiagnosticRule> repositoryRules)
{
    private readonly IReadOnlyList<IArchitectureDiagnosticRule> architectureRules =
        architectureRules.ToArray();

    private readonly IReadOnlyList<IRepositoryDiagnosticRule> repositoryRules =
        repositoryRules.ToArray();

    public IReadOnlyList<DiagnosticFinding> Evaluate(
        RepositoryScan scan,
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(graph);

        var findings = architectureRules
            .SelectMany(rule => rule.Evaluate(graph))
            .Concat(
                repositoryRules.SelectMany(
                    rule => rule.Evaluate(scan, graph)))
            .OrderByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Title, StringComparer.Ordinal)
            .ToArray();

        return findings;
    }

    private static int SeverityRank(
        DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.High => 4,
            DiagnosticSeverity.Medium => 3,
            DiagnosticSeverity.Low => 2,
            DiagnosticSeverity.Info => 1,
            _ => 0
        };
    }
}