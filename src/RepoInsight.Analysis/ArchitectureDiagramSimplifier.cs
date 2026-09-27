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
                    ArchitectureRelationshipType.Implements or
                    ArchitectureRelationshipType.UsesDatabase or
                    ArchitectureRelationshipType.UsesExternalService)
            .GroupBy(edge => new
            {
                edge.SourceNodeId,
                edge.TargetNodeId
            })
            .Select(group => group
                .OrderBy(edge => Priority(edge.RelationshipType))
                .ThenBy(edge => edge.RelationshipType)
                .First())
            .OrderBy(edge => edge.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetNodeId, StringComparer.Ordinal)
            .ThenBy(edge => edge.RelationshipType)
            .ToArray();

        return new ArchitectureGraph(nodes)
        {
            Edges = edges
        };
    }

    private static int Priority(
        ArchitectureRelationshipType relationshipType)
    {
        return relationshipType switch
        {
            ArchitectureRelationshipType.UsesExternalService => 0,
            ArchitectureRelationshipType.UsesDatabase => 1,
            ArchitectureRelationshipType.Implements => 2,
            ArchitectureRelationshipType.Injects => 3,
            ArchitectureRelationshipType.DependsOn => 4,
            ArchitectureRelationshipType.Imports => 5,
            _ => int.MaxValue
        };
    }
}