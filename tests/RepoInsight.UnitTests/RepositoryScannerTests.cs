using RepoInsight.Analysis;

namespace RepoInsight.UnitTests;

public sealed class RepositoryScannerTests : IDisposable
{
    private readonly string repositoryPath = Directory.CreateTempSubdirectory("RepoInsight-tests-").FullName;
    private readonly IRepositoryScanner scanner = new RepositoryScanner();

    [Fact]
    public void Scan_ReturnsRelativePathsExtensionsAndByteSizesAtEveryDepth()
    {
        WriteFile("README", []);
        WriteFile(Path.Combine("src", "Program.cs"), [0, 1, 2, 255]);
        WriteFile(Path.Combine("src", "nested", "data.json"), [123, 125]);

        var result = scanner.Scan(repositoryPath);

        Assert.Equal(3, result.Files.Count);
        var program = Assert.Single(result.Files, file => file.RelativePath == Path.Combine("src", "Program.cs"));
        Assert.Equal(".cs", program.Extension);
        Assert.Equal(4L, program.SizeInBytes);
        var readme = Assert.Single(result.Files, file => file.RelativePath == "README");
        Assert.Equal(string.Empty, readme.Extension);
        Assert.Equal(0L, readme.SizeInBytes);
        var nested = Assert.Single(result.Files, file => file.RelativePath == Path.Combine("src", "nested", "data.json"));
        Assert.Equal(".json", nested.Extension);
        Assert.Equal(2L, nested.SizeInBytes);
        Assert.All(result.Files, file => Assert.False(Path.IsPathRooted(file.RelativePath)));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("dist")]
    [InlineData("build")]
    [InlineData(".next")]
    [InlineData("coverage")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("BIN")]
    public void Scan_PrunesIgnoredDirectoriesAtEveryDepth(string ignoredDirectory)
    {
        WriteFile(Path.Combine(ignoredDirectory, "nested", "ignored.txt"), [1]);
        WriteFile(Path.Combine("src", ignoredDirectory, "ignored.txt"), [1]);
        WriteFile(Path.Combine("src", "keep.cs"), [1]);
        WriteFile(Path.Combine(ignoredDirectory + "-source", "keep.txt"), [1]);

        var result = scanner.Scan(repositoryPath);

        Assert.Equal(2, result.Files.Count);
        Assert.Contains(result.Files, file => file.RelativePath == Path.Combine("src", "keep.cs"));
        Assert.Contains(result.Files, file => file.RelativePath == Path.Combine(ignoredDirectory + "-source", "keep.txt"));
    }

    [Fact]
    public void Scan_DoesNotIgnoreFilesWithIgnoredDirectoryNames()
    {
        WriteFile("build", [1]);

        Assert.Equal("build", Assert.Single(scanner.Scan(repositoryPath).Files).RelativePath);
    }

    [Fact]
    public void Scan_EmptyRepositoryReturnsNoFiles()
    {
        Assert.Empty(scanner.Scan(repositoryPath).Files);
    }

    [Fact]
    public void Scan_AcceptsRelativeDirectoryPath()
    {
        WriteFile("file.txt", [1]);

        var result = scanner.Scan(Path.GetRelativePath(Environment.CurrentDirectory, repositoryPath));

        Assert.Equal("file.txt", Assert.Single(result.Files).RelativePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid\0path")]
    public void Scan_InvalidPathThrowsArgumentException(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => scanner.Scan(path!));
    }

    [Fact]
    public void Scan_MissingDirectoryThrowsDirectoryNotFoundException()
    {
        Assert.Throws<DirectoryNotFoundException>(() => scanner.Scan(Path.Combine(repositoryPath, "missing")));
    }

    [Fact]
    public void Scan_FilePathThrowsDirectoryNotFoundException()
    {
        WriteFile("file.txt", [1]);

        Assert.Throws<DirectoryNotFoundException>(() => scanner.Scan(Path.Combine(repositoryPath, "file.txt")));
    }

    private void WriteFile(string relativePath, byte[] contents)
    {
        var path = Path.Combine(repositoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }

    public void Dispose()
    {
        Directory.Delete(repositoryPath, recursive: true);
    }
}
