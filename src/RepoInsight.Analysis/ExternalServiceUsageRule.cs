using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ExternalServiceUsageRule
    : IArchitectureDiagnosticRule
{
    public IReadOnlyList<DiagnosticFinding> Evaluate(
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodesById = graph.Nodes
            .ToDictionary(
                node => node.Id,
                StringComparer.Ordinal);

        var findings = new List<DiagnosticFinding>();

        foreach (var edge in graph.Edges
                     .Where(edge =>
                         edge.RelationshipType ==
                         ArchitectureRelationshipType.UsesExternalService)
                     .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
                     .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal))
        {
            if (!nodesById.TryGetValue(edge.SourceNodeId, out var sourceNode) ||
                !nodesById.TryGetValue(edge.TargetNodeId, out var targetNode))
            {
                continue;
            }

            findings.Add(
                new DiagnosticFinding(
                    Code: "ARCH004",
                    Title: "Component uses external service",
                    Description:
                        $"{sourceNode.DisplayName} depends on external service " +
                        $"{targetNode.DisplayName}.",
                    Severity: DiagnosticSeverity.Info,
                    Evidence: edge.Evidence,
                    RelatedNodeIds:
                    [
                        sourceNode.Id,
                        targetNode.Id
                    ]));
        }

        return findings;
    }
}