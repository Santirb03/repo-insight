using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using RepoInsight.Analysis;
using RepoInsight.Api;
using RepoInsight.Application;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ReactRepositoryValidationTests
{
    [Fact]
    public async Task PipelineAnalyzesNestAndNextZipWithoutAzureOrRepositoryExecution()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Write("apps/web/package.json", "{\"dependencies\":{\"next\":\"16\"}}");
            Write("apps/web/app/page.tsx", "export default function Home() { return <main/>; }");
            Write("apps/api/package.json", "{\"dependencies\":{\"@nestjs/core\":\"10\"}}");
            Write("apps/api/auth.service.ts", "@Injectable() export class AuthService {}");
            void Write(string path, string source) { using var writer = new StreamWriter(archive.CreateEntry(path).Open()); writer.Write(source); }
        }
        stream.Position = 0;
        using var services = new ServiceCollection().AddRepositoryScanning().BuildServiceProvider();
        var result = await services.GetRequiredService<IRepositoryAnalysisService>().AnalyzeAsync(stream);
        Assert.Single(result.Architecture.Nodes, n => n.NodeType == ArchitectureNodeType.Page);
        Assert.Single(result.Architecture.Nodes, n => n.NodeType == ArchitectureNodeType.Service);
        Assert.Contains("Home", result.Mermaid);
        Assert.Contains("AuthService", result.Mermaid);
        Assert.NotEmpty(result.Narrative.Summary);
    }

    [Fact]
    public void RecognizesRepoInsightsOwnFrontendWithoutExecutingIt()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RepoInsight.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var web = Path.Combine(directory.FullName, "src", "RepoInsight.Web");
        Assert.True(Directory.Exists(web));
        var scan = new RepositoryScanner().Scan(web);
        var technologies = new TechnologyDetector().Detect(web, scan);
        Assert.Contains(technologies.Technologies, t => t.Name == "Next.js");
        var graph = new ReactArchitectureAnalyzer().Analyze(web, scan);
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Page && n.RelativeSourcePath.Replace('\\', '/') == "app/page.tsx");
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Layout && n.RelativeSourcePath.Replace('\\', '/') == "app/layout.tsx");
        var workspace = Assert.Single(graph.Nodes, n => n.DisplayName == "RepositoryWorkspace");
        var upload = Assert.Single(graph.Nodes, n => n.DisplayName == "UploadPanel");
        Assert.Contains(graph.Edges, e => e.SourceNodeId == workspace.Id && e.TargetNodeId == upload.Id && e.RelationshipType == ArchitectureRelationshipType.DependsOn);
    }
}
