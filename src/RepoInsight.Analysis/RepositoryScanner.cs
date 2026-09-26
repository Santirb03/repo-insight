using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "dist", "build", ".next", "coverage", "bin", "obj"
    };

    public RepositoryScan Scan(string repositoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var root = new DirectoryInfo(Path.GetFullPath(repositoryPath));
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Repository directory does not exist: {repositoryPath}");
        }

        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("Repository directory must not be a reparse point.", nameof(repositoryPath));
        }

        var files = new List<RepositoryFile>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.TryPop(out var directory))
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                // Do not follow symbolic links or junctions, including links to files.
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo child)
                {
                    if (!IgnoredDirectories.Contains(child.Name))
                    {
                        pending.Push(child);
                    }
                }
                else if (entry is FileInfo file)
                {
                    files.Add(new RepositoryFile(
                        Path.GetRelativePath(root.FullName, file.FullName),
                        file.Extension,
                        file.Length));
                }
            }
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return new RepositoryScan(files.AsReadOnly());
    }
}
