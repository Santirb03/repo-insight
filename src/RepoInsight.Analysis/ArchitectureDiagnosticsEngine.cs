using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ArchitectureDiagnosticsEngine(
    IEnumerable<IArchitectureDiagnosticRule> rules)
{
    private readonly IReadOnlyList<IArchitectureDiagnosticRule> rules =
        rules.ToArray();

    public IReadOnlyList<DiagnosticFinding> Evaluate(
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return rules
            .SelectMany(rule => rule.Evaluate(graph))
            .OrderByDescending(finding => SeverityRank(finding.Severity))
            .ThenBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Title, StringComparer.Ordinal)
            .ToArray();
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