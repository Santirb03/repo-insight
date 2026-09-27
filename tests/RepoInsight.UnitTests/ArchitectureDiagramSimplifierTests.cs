using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ArchitectureDiagramSimplifierTests
{
    [Fact]
    public void Simplify_RemovesNonVisualNoise()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "controller",
                "AuthController",
                ArchitectureNodeType.Controller,
                "auth.controller.ts"),

            new ArchitectureNode(
                "service",
                "AuthService",
                ArchitectureNodeType.Service,
                "auth.service.ts"),

            new ArchitectureNode(
                "dto",
                "LoginDto",
                ArchitectureNodeType.Dto,
                "login.dto.ts"),

            new ArchitectureNode(
                "module",
                "AuthModule",
                ArchitectureNodeType.Module,
                "auth.module.ts")
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    "controller",
                    "service",
                    ArchitectureRelationshipType.Injects,
                    ["constructor injection"]),

                new ArchitectureEdge(
                    "module",
                    "controller",
                    ArchitectureRelationshipType.DependsOn,
                    ["module controller"])
            ]
        };

        var simplifier = new ArchitectureDiagramSimplifier();

        var result = simplifier.Simplify(graph);

        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, node => node.Id == "controller");
        Assert.Contains(result.Nodes, node => node.Id == "service");

        Assert.Single(result.Edges);

        Assert.Equal(
            ArchitectureRelationshipType.Injects,
            result.Edges[0].RelationshipType);
    }

    [Fact]
    public void Simplify_KeepsDatabaseAndExternalRelationships()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "service",
                "PaymentsService",
                ArchitectureNodeType.Service,
                "payments.service.ts"),

            new ArchitectureNode(
                "database",
                "PrismaService",
                ArchitectureNodeType.DataAccessService,
                "prisma.service.ts"),

            new ArchitectureNode(
                "stripe",
                "Stripe",
                ArchitectureNodeType.ExternalService,
                "payments.service.ts")
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    "service",
                    "database",
                    ArchitectureRelationshipType.UsesDatabase,
                    ["database dependency"]),

                new ArchitectureEdge(
                    "service",
                    "stripe",
                    ArchitectureRelationshipType.UsesExternalService,
                    ["stripe client"])
            ]
        };

        var simplifier = new ArchitectureDiagramSimplifier();

        var result = simplifier.Simplify(graph);

        Assert.Equal(3, result.Nodes.Count);
        Assert.Equal(2, result.Edges.Count);
    }

    [Fact]
    public void Simplify_RemovesEdgesToExcludedNodes()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "service",
                "AuthService",
                ArchitectureNodeType.Service,
                "auth.service.ts"),

            new ArchitectureNode(
                "guard",
                "JwtAuthGuard",
                ArchitectureNodeType.Guard,
                "jwt-auth.guard.ts")
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    "service",
                    "guard",
                    ArchitectureRelationshipType.DependsOn,
                    ["test"])
            ]
        };

        var simplifier = new ArchitectureDiagramSimplifier();

        var result = simplifier.Simplify(graph);

        Assert.Single(result.Nodes);
        Assert.Empty(result.Edges);
    }
}