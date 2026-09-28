using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class MissingComponentTestRuleTests
{
    [Fact]
    public void Evaluate_FlagsComponentWithoutMatchingTest()
    {
        var service = new ArchitectureNode(
            "service",
            "UsersService",
            ArchitectureNodeType.Service,
            "src/users/users.service.ts");

        var scan = new RepositoryScan(
        [
            new RepositoryFile(
                "src/users/users.service.ts",
                ".ts",
                100)
        ]);

        var graph = new ArchitectureGraph(
        [
            service
        ]);

        var rule = new MissingComponentTestRule();

        var findings = rule.Evaluate(scan, graph);

        var finding = Assert.Single(findings);

        Assert.Equal("TEST001", finding.Code);
        Assert.Equal(
            DiagnosticSeverity.Low,
            finding.Severity);
        Assert.Contains(
            service.Id,
            finding.RelatedNodeIds);
    }

    [Fact]
    public void Evaluate_DoesNotFlagComponentWithSpecFile()
    {
        var service = new ArchitectureNode(
            "service",
            "UsersService",
            ArchitectureNodeType.Service,
            "src/users/users.service.ts");

        var scan = new RepositoryScan(
        [
            new RepositoryFile(
                "src/users/users.service.ts",
                ".ts",
                100),

            new RepositoryFile(
                "src/users/users.service.spec.ts",
                ".ts",
                100)
        ]);

        var graph = new ArchitectureGraph(
        [
            service
        ]);

        var rule = new MissingComponentTestRule();

        var findings = rule.Evaluate(scan, graph);

        Assert.Empty(findings);
    }

    [Fact]
    public void Evaluate_DoesNotFlagComponentWithTestFile()
    {
        var controller = new ArchitectureNode(
            "controller",
            "UsersController",
            ArchitectureNodeType.Controller,
            "src/users/users.controller.ts");

        var scan = new RepositoryScan(
        [
            new RepositoryFile(
                "src/users/users.controller.ts",
                ".ts",
                100),

            new RepositoryFile(
                "src/users/users.controller.test.ts",
                ".ts",
                100)
        ]);

        var graph = new ArchitectureGraph(
        [
            controller
        ]);

        var rule = new MissingComponentTestRule();

        var findings = rule.Evaluate(scan, graph);

        Assert.Empty(findings);
    }
}