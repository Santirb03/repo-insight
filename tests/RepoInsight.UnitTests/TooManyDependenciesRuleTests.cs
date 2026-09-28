using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class TooManyDependenciesRuleTests
{
    [Fact]
    public void Evaluate_FlagsComponentWithFiveDependencies()
    {
        var service = new ArchitectureNode(
            "service",
            "LargeService",
            ArchitectureNodeType.Service,
            "large.service.ts");

        var dependencies = Enumerable.Range(1, 5)
            .Select(index =>
                new ArchitectureNode(
                    $"dep-{index}",
                    $"Dependency{index}",
                    ArchitectureNodeType.Service,
                    $"dep-{index}.ts"))
            .ToArray();

        var graph = new ArchitectureGraph(
            [service, .. dependencies])
        {
            Edges = dependencies
                .Select(dependency =>
                    new ArchitectureEdge(
                        service.Id,
                        dependency.Id,
                        ArchitectureRelationshipType.Injects,
                        ["constructor injection"]))
                .ToArray()
        };

        var rule = new TooManyDependenciesRule();

        var findings = rule.Evaluate(graph);

        var finding = Assert.Single(findings);

        Assert.Equal("ARCH002", finding.Code);
        Assert.Equal(DiagnosticSeverity.Medium, finding.Severity);
        Assert.Contains(service.Id, finding.RelatedNodeIds);
    }

    [Fact]
    public void Evaluate_DoesNotFlagFourDependencies()
    {
        var service = new ArchitectureNode(
            "service",
            "SmallService",
            ArchitectureNodeType.Service,
            "small.service.ts");

        var dependencies = Enumerable.Range(1, 4)
            .Select(index =>
                new ArchitectureNode(
                    $"dep-{index}",
                    $"Dependency{index}",
                    ArchitectureNodeType.Service,
                    $"dep-{index}.ts"))
            .ToArray();

        var graph = new ArchitectureGraph(
            [service, .. dependencies])
        {
            Edges = dependencies
                .Select(dependency =>
                    new ArchitectureEdge(
                        service.Id,
                        dependency.Id,
                        ArchitectureRelationshipType.Injects,
                        ["constructor injection"]))
                .ToArray()
        };

        var rule = new TooManyDependenciesRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_CountsUniqueTargetsOnly()
    {
        var service = new ArchitectureNode(
            "service",
            "Service",
            ArchitectureNodeType.Service,
            "service.ts");

        var dependency = new ArchitectureNode(
            "database",
            "PrismaService",
            ArchitectureNodeType.DataAccessService,
            "prisma.service.ts");

        var graph = new ArchitectureGraph(
            [service, dependency])
        {
            Edges =
            [
                new ArchitectureEdge(
                    service.Id,
                    dependency.Id,
                    ArchitectureRelationshipType.Injects,
                    ["constructor injection"]),

                new ArchitectureEdge(
                    service.Id,
                    dependency.Id,
                    ArchitectureRelationshipType.UsesDatabase,
                    ["database dependency"])
            ]
        };

        var rule = new TooManyDependenciesRule();

        var findings = rule.Evaluate(graph);

        Assert.Empty(findings);
    }
}