using RepoInsight.Analysis.Mermaid;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class MermaidDiagramRendererTests
{
    private readonly IMermaidDiagramRenderer renderer =
        new MermaidDiagramRenderer();

    [Fact]
    public void EmptyGraph_RendersEmptyFlowchart()
    {
        var graph = new ArchitectureGraph([]);

        var result = renderer.Render(graph);

        Assert.Equal("flowchart LR", result);
    }

    [Fact]
    public void SingleNode_IsRendered()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "auth-service",
                "AuthService",
                ArchitectureNodeType.Service,
                "src/auth/auth.service.ts")
        ]);

        var result = renderer.Render(graph);

        Assert.Contains(
            "node_auth_service[\"AuthService\"]",
            result);
    }

    [Fact]
    public void DependencyEdge_IsRendered()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "auth-controller",
                "AuthController",
                ArchitectureNodeType.Controller,
                "src/auth/auth.controller.ts"),

            new ArchitectureNode(
                "auth-service",
                "AuthService",
                ArchitectureNodeType.Service,
                "src/auth/auth.service.ts")
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    "auth-controller",
                    "auth-service",
                    ArchitectureRelationshipType.Injects,
                    ["constructor injection"])
            ]
        };

        var result = renderer.Render(graph);

        Assert.Contains(
            "node_auth_controller -->|injects| node_auth_service",
            result);
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "b",
                "B",
                ArchitectureNodeType.Service,
                "b.ts"),

            new ArchitectureNode(
                "a",
                "A",
                ArchitectureNodeType.Controller,
                "a.ts")
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    "b",
                    "a",
                    ArchitectureRelationshipType.DependsOn,
                    ["test"])
            ]
        };

        var first = renderer.Render(graph);
        var second = renderer.Render(graph);

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnsafeCharactersInIds_AreNormalized()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "auth/service.v1",
                "AuthService",
                ArchitectureNodeType.Service,
                "auth.service.ts")
        ]);

        var result = renderer.Render(graph);

        Assert.Contains(
            "node_auth_service_v1[\"AuthService\"]",
            result);
    }

    [Fact]
    public void Labels_AreEscaped()
    {
        var graph = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "special",
                "Service \"A\"",
                ArchitectureNodeType.Service,
                "service.ts")
        ]);

        var result = renderer.Render(graph);

        Assert.Contains(
            "Service \\\"A\\\"",
            result);
    }
}