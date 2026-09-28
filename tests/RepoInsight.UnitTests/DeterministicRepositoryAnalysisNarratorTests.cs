using RepoInsight.Application;
using RepoInsight.Domain;
using RepoInsight.Infrastructure;

namespace RepoInsight.UnitTests;

public sealed class DeterministicRepositoryAnalysisNarratorTests
{
    [Fact]
    public async Task GenerateAsync_CreatesStructuredNarrative()
    {
        var technologies = new TechnologyProfile(
        [
            new DetectedTechnology(
                "NestJS",
                TechnologyCategory.Framework,
                TechnologyConfidence.High,
                ["package.json"])
        ]);

        var architecture = new ArchitectureGraph(
        [
            new ArchitectureNode(
                "service",
                "UsersService",
                ArchitectureNodeType.Service,
                "users.service.ts")
        ]);

        var diagnostics = new DiagnosticFinding[]
        {
            new(
                "ARCH001",
                "Controller accesses database directly",
                "Test",
                DiagnosticSeverity.High,
                ["evidence"],
                ["node-1"]),

            new(
                "TEST001",
                "No nearby test file detected",
                "Test",
                DiagnosticSeverity.Low,
                ["evidence"],
                ["node-2"])
        };

        IRepositoryAnalysisNarrator narrator =
            new DeterministicRepositoryAnalysisNarrator();

        var result = await narrator.GenerateAsync(
            technologies,
            architecture,
            diagnostics);

        Assert.Contains(
            "Detected 1 technologies",
            result.Summary);

        Assert.NotEmpty(result.Strengths);
        Assert.Contains(
            result.Risks,
            risk => risk.Contains("ARCH001"));

        Assert.NotEmpty(result.Recommendations);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotCreateHighRiskRecommendationWithoutHighFindings()
    {
        var technologies = new TechnologyProfile([]);
        var architecture = new ArchitectureGraph([]);
        var diagnostics = Array.Empty<DiagnosticFinding>();

        IRepositoryAnalysisNarrator narrator =
            new DeterministicRepositoryAnalysisNarrator();

        var result = await narrator.GenerateAsync(
            technologies,
            architecture,
            diagnostics);

        Assert.DoesNotContain(
            result.Recommendations,
            recommendation =>
                recommendation.Contains(
                    "high-severity",
                    StringComparison.OrdinalIgnoreCase));
    }
}