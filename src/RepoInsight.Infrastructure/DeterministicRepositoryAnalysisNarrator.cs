using RepoInsight.Application;
using RepoInsight.Domain;

namespace RepoInsight.Infrastructure;

public sealed class DeterministicRepositoryAnalysisNarrator
    : IRepositoryAnalysisNarrator
{
    public Task<RepositoryNarrative> GenerateAsync(
        TechnologyProfile technologies,
        ArchitectureGraph architecture,
        IReadOnlyList<DiagnosticFinding> diagnostics,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(technologies);
        ArgumentNullException.ThrowIfNull(architecture);
        ArgumentNullException.ThrowIfNull(diagnostics);

        cancellationToken.ThrowIfCancellationRequested();

        var highCount = diagnostics.Count(
            finding => finding.Severity == DiagnosticSeverity.High);

        var mediumCount = diagnostics.Count(
            finding => finding.Severity == DiagnosticSeverity.Medium);

        var lowCount = diagnostics.Count(
            finding => finding.Severity == DiagnosticSeverity.Low);

        var infoCount = diagnostics.Count(
            finding => finding.Severity == DiagnosticSeverity.Info);

        var summary =
            $"Detected {technologies.Technologies.Count} technologies, " +
            $"{architecture.Nodes.Count} architecture components, " +
            $"{architecture.Edges.Count} relationships, and " +
            $"{diagnostics.Count} diagnostic findings.";

        var strengths = new List<string>();

        if (technologies.Technologies.Count > 0)
        {
            strengths.Add(
                "The repository contains a detectable technology stack.");
        }

        if (architecture.Nodes.Count > 0)
        {
            strengths.Add(
                "Architectural components and relationships were successfully reconstructed.");
        }

        var risks = diagnostics
            .Where(finding =>
                finding.Severity is
                    DiagnosticSeverity.High or
                    DiagnosticSeverity.Medium)
            .Select(finding =>
                $"{finding.Code}: {finding.Title}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var recommendations = new List<string>();

        if (highCount > 0)
        {
            recommendations.Add(
                "Review high-severity architectural findings first.");
        }

        if (mediumCount > 0)
        {
            recommendations.Add(
                "Review components with elevated architectural coupling.");
        }

        if (lowCount > 0)
        {
            recommendations.Add(
                "Review low-severity findings for testing and maintainability improvements.");
        }

        if (infoCount > 0)
        {
            recommendations.Add(
                "Document important external integrations and operational dependencies.");
        }

        var narrative = new RepositoryNarrative(
            summary,
            strengths,
            risks,
            recommendations);

        return Task.FromResult(narrative);
    }
}