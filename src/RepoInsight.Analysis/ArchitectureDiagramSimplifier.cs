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

    private static readonly HashSet<ArchitectureNodeType> FrontendTypes =
    [
        ArchitectureNodeType.Component, ArchitectureNodeType.Hook, ArchitectureNodeType.Context,
        ArchitectureNodeType.Provider, ArchitectureNodeType.Page, ArchitectureNodeType.Layout,
        ArchitectureNodeType.Loading, ArchitectureNodeType.ErrorBoundary, ArchitectureNodeType.RouteHandler,
        ArchitectureNodeType.ServerAction, ArchitectureNodeType.ApiClient, ArchitectureNodeType.FrontendService
    ];

    private static bool IsExpress(ArchitectureNode node) => node.Id.StartsWith("express:", StringComparison.Ordinal);

    public ArchitectureGraph Simplify(ArchitectureGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes
            .Where(node => IncludedNodeTypes.Contains(node.NodeType) || FrontendTypes.Contains(node.NodeType) ||
                node.NodeType == ArchitectureNodeType.Middleware && node.Id.StartsWith("react:", StringComparison.Ordinal))
            .Concat(graph.Nodes.Where(n => IsExpress(n) && n.NodeType is ArchitectureNodeType.ExpressApplication or ArchitectureNodeType.Router or ArchitectureNodeType.HttpRoute or ArchitectureNodeType.RouteHandler or ArchitectureNodeType.Middleware or ArchitectureNodeType.ErrorMiddleware or ArchitectureNodeType.Repository or ArchitectureNodeType.ApiClient or ArchitectureNodeType.Bootstrap))
            .DistinctBy(n => n.Id)
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var frontendIds = nodes.Where(n => !IsExpress(n) && (n.Id.StartsWith("react:", StringComparison.Ordinal) || FrontendTypes.Contains(n.NodeType)))
            .Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        // Source modules and utilities remain in the full graph; visual frontend nodes are capped.
        var keptFrontendIds = nodes.Where(n => frontendIds.Contains(n.Id))
            .OrderBy(n => n.NodeType is ArchitectureNodeType.Page or ArchitectureNodeType.Layout or ArchitectureNodeType.RouteHandler ? 0 : 1)
            .ThenBy(n => n.Id, StringComparer.Ordinal).Take(80).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        nodes = nodes.Where(n => !frontendIds.Contains(n.Id) || keptFrontendIds.Contains(n.Id)).ToArray();
        var expressIds = nodes.Where(IsExpress).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var keptExpressIds = nodes.Where(IsExpress).OrderBy(n => n.NodeType is ArchitectureNodeType.ExpressApplication or ArchitectureNodeType.Router ? 0 : 1)
            .ThenBy(n => n.Id, StringComparer.Ordinal).Take(80).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        nodes = nodes.Where(n => !expressIds.Contains(n.Id) || keptExpressIds.Contains(n.Id)).ToArray();
        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var edges = graph.Edges
            .Where(edge =>
                nodeIds.Contains(edge.SourceNodeId) &&
                nodeIds.Contains(edge.TargetNodeId))
            .Where(edge =>
                edge.RelationshipType is
                    ArchitectureRelationshipType.Injects or
                    ArchitectureRelationshipType.Implements or
                    ArchitectureRelationshipType.UsesDatabase or
                    ArchitectureRelationshipType.UsesExternalService ||
                edge.RelationshipType == ArchitectureRelationshipType.DependsOn && (frontendIds.Contains(edge.SourceNodeId) || expressIds.Contains(edge.SourceNodeId)))
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
