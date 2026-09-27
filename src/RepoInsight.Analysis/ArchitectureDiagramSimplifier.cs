using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class ArchitectureDiagramSimplifier
{
    private static readonly HashSet<ArchitectureNodeType> IncludedNodeTypes =
    [
        ArchitectureNodeType.Controller,
        ArchitectureNodeType.WebhookController,
        ArchitectureNodeType.Service,
        ArchitectureNodeType.DataAccessService,
        ArchitectureNodeType.ExternalService
    ];

    public ArchitectureGraph Simplify(ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes
            .Where(node => IncludedNodeTypes.Contains(node.NodeType))
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var nodeIds = nodes
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var edges = graph.Edges
            .Where(edge =>
                nodeIds.Contains(edge.SourceNodeId) &&
                nodeIds.Contains(edge.TargetNodeId))
            .Where(edge =>
                edge.RelationshipType is
                    ArchitectureRelationshipType.Injects or
                    ArchitectureRelationshipType.UsesDatabase or
                    ArchitectureRelationshipType.UsesExternalService)
            .GroupBy(edge => new
            {
                edge.SourceNodeId,
                edge.TargetNodeId,
                edge.RelationshipType
            })
            .Select(group => group.First())
            .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.RelationshipType)
            .ToArray();

        return new ArchitectureGraph(nodes)
        {
            Edges = edges
        };
    }
}