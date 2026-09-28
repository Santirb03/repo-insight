namespace RepoInsight.Domain;

public sealed record DiagnosticFinding(
    string Code,
    string Title,
    string Description,
    DiagnosticSeverity Severity,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> RelatedNodeIds);