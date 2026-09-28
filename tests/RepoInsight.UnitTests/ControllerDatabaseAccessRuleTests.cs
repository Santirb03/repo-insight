using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ControllerDatabaseAccessRuleTests
{
    [Fact]
    public void Evaluate_FindsControllerDatabaseAccess()
    {
        var controller = new ArchitectureNode(
            "controller",
            "HealthController",
            ArchitectureNodeType.Controller,
            "health.controller.ts");

        var database = new ArchitectureNode(
            "database",
            "PrismaService",
            ArchitectureNodeType.DataAccessService,
            "prisma.service.ts");

        var graph = new ArchitectureGraph(
        [
            controller,
            database
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    controller.Id,
                    database.Id,
                    ArchitectureRelationshipType.UsesDatabase,
                    ["HealthController constructor parameter PrismaService"])
            ]
        };

        var rule = new ControllerDatabaseAccessRule();

        var findings = rule.Evaluate(graph);

        var finding = Assert.Single(findings);

        Assert.Equal("ARCH001", finding.Code);
        Assert.Equal(
            DiagnosticSeverity.High,
            finding.Severity);

        Assert.Contains(
            controller.Id,
            finding.RelatedNodeIds);

        Assert.Contains(
            database.Id,
            finding.RelatedNodeIds);
    }

    [Fact]
    public void Evaluate_DoesNotFlagServiceDatabaseAccess()
    {
        var service = new ArchitectureNode(
            "service",
            "AuthService",
            ArchitectureNodeType.Service,
            "auth.service.ts");

        var database = new ArchitectureNode(
            "database",
            "PrismaService",
            ArchitectureNodeType.DataAccessService,
            "prisma.service.ts");

        var graph = new ArchitectureGraph(
        [
            service,
            database
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    service.Id,
                    database.Id,
                    ArchitectureRelationshipType.UsesDatabase,
                    ["AuthService constructor parameter PrismaService"])
            ]
        };

        var rule = new ControllerDatabaseAccessRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_IgnoresNonDatabaseRelationships()
    {
        var controller = new ArchitectureNode(
            "controller",
            "AuthController",
            ArchitectureNodeType.Controller,
            "auth.controller.ts");

        var service = new ArchitectureNode(
            "service",
            "AuthService",
            ArchitectureNodeType.Service,
            "auth.service.ts");

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

        var rule = new ControllerDatabaseAccessRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }
}