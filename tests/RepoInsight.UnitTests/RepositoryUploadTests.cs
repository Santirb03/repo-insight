using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepoInsight.Analysis;
using RepoInsight.Api;
using RepoInsight.Api.Services;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class RepositoryUploadTests : IAsyncLifetime
{
    private readonly WebApplication app;
    private readonly RecordingScanner scanner = new();
    private readonly HashSet<string> existingDirectories = GetUploadDirectories();
    private HttpClient client = null!;

    public RepositoryUploadTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
        builder.Services.AddRepositoryScanning();
        builder.Services.AddSingleton<IRepositoryScanner>(scanner);
        app = builder.Build();
        app.MapRepositoryEndpoints();
    }

    public async Task InitializeAsync()
    {
        await app.StartAsync();
        client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
    }

    [Fact]
    public async Task ValidZip_ReturnsScanAndCleansUp()
    {
        using var form = Upload(CreateZip("src/Program.cs"), "repository.ZIP");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<RepositoryScan>();
        var file = Assert.Single(Assert.IsType<RepositoryScan>(scan).Files);
        Assert.Equal(Path.Combine("src", "Program.cs"), file.RelativePath);
        Assert.Equal(".cs", file.Extension);
        Assert.Equal(3L, file.SizeInBytes);
        Assert.NotNull(scanner.Path);
        Assert.False(Directory.Exists(scanner.Path));
    }

    [Theory]
    [InlineData("repository.txt")]
    [InlineData("repository.zip.exe")]
    public async Task NonZipExtension_IsRejected(string filename)
    {
        using var form = Upload(CreateZip("file.cs"), filename);
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
    }

    [Fact]
    public async Task NoFile_IsRejected()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("value"), "description");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        using var form = Upload([], "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MultipleFiles_AreRejected()
    {
        using var form = Upload(CreateZip("file.cs"), "first.zip");
        form.Add(new ByteArrayContent(CreateZip("other.cs")), "file", "second.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(64)]
    public async Task InvalidZipContent_IsRejectedAndCleanedUp(int length)
    {
        using var form = Upload(new byte[length], "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
    }

    [Theory]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("nested/../../")]
    [InlineData("/")]
    [InlineData("C:/")]
    [InlineData(".. /")]
    public async Task UnsafePath_IsRejectedAndPartialExtractionIsCleanedUp(string prefix)
    {
        var escapedName = $"RepoInsight-escape-{Guid.NewGuid():N}.txt";
        using var form = Upload(CreateZip("safe.cs", prefix + escapedName), "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
        Assert.False(File.Exists(System.IO.Path.Combine(System.IO.Path.GetTempPath(), escapedName)));
    }

    [Fact]
    public async Task IgnoredDirectories_AreExcludedAfterExtraction()
    {
        string[] ignored = [".git", "node_modules", "dist", "build", ".next", "coverage", "bin", "obj"];
        var entries = ignored.SelectMany(name => new[] { $"{name}/ignored.cs", $"src/{name}/ignored.cs" })
            .Append("src/keep.cs").ToArray();
        using var form = Upload(CreateZip(entries), "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<RepositoryScan>();
        Assert.Equal(Path.Combine("src", "keep.cs"), Assert.Single(scan!.Files).RelativePath);
    }

    [Fact]
    public async Task SymbolicLinkEntry_IsRejected()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("../outside");
        }

        using var form = Upload(stream.ToArray(), "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(scanner.Path);
    }

    [Fact]
    public async Task EmptyArchive_ReturnsEmptyScan()
    {
        using var form = Upload(CreateZip(), "repository.zip");
        using var response = await client.PostAsync("/api/repositories/scan", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scan = await response.Content.ReadFromJsonAsync<RepositoryScan>();
        Assert.Empty(scan!.Files);
    }

    [Fact]
    public async Task ScannerFailure_StillCleansUp()
    {
        scanner.Fail = true;
        using var stream = new MemoryStream(CreateZip("file.cs"));
        var service = new RepositoryZipService(scanner);

        await Assert.ThrowsAsync<IOException>(() => service.ScanAsync(stream));

        Assert.NotNull(scanner.Path);
        Assert.False(Directory.Exists(scanner.Path));
    }

    [Fact]
    public async Task Cancellation_StillCleansUp()
    {
        using var stream = new MemoryStream(CreateZip("file.cs"));
        var service = new RepositoryZipService(scanner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(stream, cancellation.Token));
        Assert.Null(scanner.Path);
    }

    [Fact]
    public void Registration_ResolvesExistingScanner()
    {
        using var services = new ServiceCollection().AddRepositoryScanning().BuildServiceProvider();
        using var scope = services.CreateScope();
        Assert.IsType<RepositoryScanner>(scope.ServiceProvider.GetRequiredService<IRepositoryScanner>());
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string filename)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", filename);
        return form;
    }

    private static byte[] CreateZip(params string[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                using var output = archive.CreateEntry(name).Open();
                output.Write([1, 2, 3]);
            }
        }

        return stream.ToArray();
    }

    private static HashSet<string> GetUploadDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "RepoInsight-upload-*").ToHashSet();

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
        Assert.Empty(GetUploadDirectories().Except(existingDirectories));
    }

    private sealed class RecordingScanner : IRepositoryScanner
    {
        public string? Path { get; private set; }
        public bool Fail { get; set; }

        public RepositoryScan Scan(string repositoryPath)
        {
            Path = repositoryPath;
            if (Fail)
            {
                throw new IOException("Simulated scanner failure.");
            }

            return new RepositoryScanner().Scan(repositoryPath);
        }
    }
}
