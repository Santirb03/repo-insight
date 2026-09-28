using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class TooManyDependenciesRule
    : IArchitectureDiagnosticRule
{
    private const int DependencyThreshold = 5;

    public IReadOnlyList<DiagnosticFinding> Evaluate(
        ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodesById = graph.Nodes
            .ToDictionary(
                node => node.Id,
                StringComparer.Ordinal);

        var dependencyCounts = graph.Edges
            .Where(edge =>
                edge.RelationshipType is
                    ArchitectureRelationshipType.DependsOn or
                    ArchitectureRelationshipType.Injects or
                    ArchitectureRelationshipType.UsesDatabase or
                    ArchitectureRelationshipType.UsesExternalService)
            .GroupBy(edge => edge.SourceNodeId)
            .Select(group => new
            {
                SourceNodeId = group.Key,
                DependencyIds = group
                    .Select(edge => edge.TargetNodeId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item =>
                item.DependencyIds.Length >= DependencyThreshold)
            .OrderBy(
                item => item.SourceNodeId,
                StringComparer.Ordinal)
            .ToArray();

        var findings = new List<DiagnosticFinding>();

        foreach (var item in dependencyCounts)
        {
            if (!nodesById.TryGetValue(
                    item.SourceNodeId,
                    out var sourceNode))
            {
                continue;
            }

            if (sourceNode.NodeType is not
                ArchitectureNodeType.Service and not
                ArchitectureNodeType.Controller and not
                ArchitectureNodeType.WebhookController)
            {
                continue;
            }

            findings.Add(
                new DiagnosticFinding(
                    Code: "ARCH002",
                    Title: "Component has many dependencies",
                    Description:
                        $"{sourceNode.DisplayName} depends on " +
                        $"{item.DependencyIds.Length} different components. " +
                        "Consider splitting responsibilities or introducing clearer boundaries.",
                    Severity: DiagnosticSeverity.Medium,
                    Evidence:
                    [
                        $"{sourceNode.DisplayName} has " +
                        $"{item.DependencyIds.Length} unique outgoing dependencies."
                    ],
                    RelatedNodeIds:
                    [
                        sourceNode.Id,
                        .. item.DependencyIds
                    ]));
        }

        return findings;
    }
}