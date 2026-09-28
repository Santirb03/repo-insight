using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class OrphanComponentRuleTests
{
    [Fact]
    public void Evaluate_FlagsIsolatedService()
    {
        var service = new ArchitectureNode(
            "service",
            "UnusedService",
            ArchitectureNodeType.Service,
            "unused.service.ts");

        var graph = new ArchitectureGraph(
        [
            service
        ]);

        var rule = new OrphanComponentRule();

        var findings = rule.Evaluate(graph);

        var finding = Assert.Single(findings);

        Assert.Equal("ARCH003", finding.Code);
        Assert.Equal(
            DiagnosticSeverity.Low,
            finding.Severity);

        Assert.Contains(
            service.Id,
            finding.RelatedNodeIds);
    }

    [Fact]
    public void Evaluate_DoesNotFlagConnectedService()
    {
        var controller = new ArchitectureNode(
            "controller",
            "UsersController",
            ArchitectureNodeType.Controller,
            "users.controller.ts");

        var service = new ArchitectureNode(
            "service",
            "UsersService",
            ArchitectureNodeType.Service,
            "users.service.ts");

        var graph = new ArchitectureGraph(
        [
            controller,
            service
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    controller.Id,
                    service.Id,
                    ArchitectureRelationshipType.Injects,
                    ["constructor injection"])
            ]
        };

        var rule = new OrphanComponentRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_IgnoresIsolatedDto()
    {
        var dto = new ArchitectureNode(
            "dto",
            "CreateUserDto",
            ArchitectureNodeType.Dto,
            "create-user.dto.ts");

        var graph = new ArchitectureGraph(
        [
            dto
        ]);

        var rule = new OrphanComponentRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }
}