using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ExternalServiceUsageRuleTests
{
    [Fact]
    public void Evaluate_FindsExternalServiceUsage()
    {
        var service = new ArchitectureNode(
            "payments-service",
            "PaymentsService",
            ArchitectureNodeType.Service,
            "payments.service.ts");

        var external = new ArchitectureNode(
            "stripe",
            "Stripe",
            ArchitectureNodeType.ExternalService,
            "payments.service.ts");

        var graph = new ArchitectureGraph(
        [
            service,
            external
        ])
        {
            Edges =
            [
                new ArchitectureEdge(
                    service.Id,
                    external.Id,
                    ArchitectureRelationshipType.UsesExternalService,
                    ["new Stripe(...)"])
            ]
        };

        var rule = new ExternalServiceUsageRule();

        var findings = rule.Evaluate(graph);

        var finding = Assert.Single(findings);

        Assert.Equal("ARCH004", finding.Code);
        Assert.Equal(DiagnosticSeverity.Info, finding.Severity);

        Assert.Contains(service.Id, finding.RelatedNodeIds);
        Assert.Contains(external.Id, finding.RelatedNodeIds);
    }

    [Fact]
    public void Evaluate_IgnoresNonExternalRelationships()
    {
        var controller = new ArchitectureNode(
            "controller",
            "PaymentsController",
            ArchitectureNodeType.Controller,
            "payments.controller.ts");

        var service = new ArchitectureNode(
            "service",
            "PaymentsService",
            ArchitectureNodeType.Service,
            "payments.service.ts");

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

        var rule = new ExternalServiceUsageRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }
}