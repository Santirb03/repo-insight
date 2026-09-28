using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class OrphanComponentRule
    : IArchitectureDiagnosticRule
{
    public IReadOnlyList<DiagnosticFinding> Evaluate(
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var connectedNodeIds = graph.Edges
            .SelectMany(edge => new[]
            {
                edge.SourceNodeId,
                edge.TargetNodeId
            })
            .ToHashSet(StringComparer.Ordinal);

        return graph.Nodes
            .Where(node =>
                node.NodeType is
                    ArchitectureNodeType.Controller or
                    ArchitectureNodeType.WebhookController or
                    ArchitectureNodeType.Service)
            .Where(node =>
                !connectedNodeIds.Contains(node.Id))
            .OrderBy(
                node => node.DisplayName,
                StringComparer.Ordinal)
            .Select(node =>
                new DiagnosticFinding(
                    Code: "ARCH003",
                    Title: "Component has no detected relationships",
                    Description:
                        $"{node.DisplayName} has no detected architectural relationships. " +
                        "This may indicate an isolated component or a relationship the analyzer could not detect.",
                    Severity: DiagnosticSeverity.Low,
                    Evidence:
                    [
                        $"No incoming or outgoing relationships were detected for {node.DisplayName}."
                    ],
                    RelatedNodeIds:
                    [
                        node.Id
                    ]))
            .ToArray();
    }
}