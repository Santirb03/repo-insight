using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ControllerDatabaseAccessRule
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

        foreach (var edge in graph.Edges)
        {
            if (edge.RelationshipType !=
                ArchitectureRelationshipType.UsesDatabase)
            {
                continue;
            }

            if (!nodesById.TryGetValue(
                    edge.SourceNodeId,
                    out var sourceNode) ||
                !nodesById.TryGetValue(
                    edge.TargetNodeId,
                    out var targetNode))
            {
                continue;
            }

            if (sourceNode.NodeType is not
                ArchitectureNodeType.Controller and not
                ArchitectureNodeType.WebhookController)
            {
                continue;
            }

            findings.Add(
                new DiagnosticFinding(
                    Code: "ARCH001",
                    Title: "Controller accesses database directly",
                    Description:
                        $"{sourceNode.DisplayName} accesses " +
                        $"{targetNode.DisplayName} directly. " +
                        "Consider moving database access behind a service or data-access layer.",
                    Severity: DiagnosticSeverity.High,
                    Evidence: edge.Evidence,
                    RelatedNodeIds:
                    [
                        sourceNode.Id,
                        targetNode.Id
                    ]));
        }

        return findings
            .OrderBy(
                finding => finding.Title,
                StringComparer.Ordinal)
            .ThenBy(
                finding => finding.RelatedNodeIds.FirstOrDefault(),
                StringComparer.Ordinal)
            .ToArray();
    }
}