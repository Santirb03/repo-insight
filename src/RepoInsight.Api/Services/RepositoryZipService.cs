using System.IO.Compression;
using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.Api.Services;

public sealed class RepositoryZipService(IRepositoryScanner scanner)
{
    public async Task<RepositoryScan> ScanAsync(Stream zipStream, CancellationToken cancellationToken = default)
    {
        var workingDirectory = Directory.CreateTempSubdirectory("RepoInsight-upload-");
        try
        {
            using var archive = OpenArchive(zipStream);
            var root = workingDirectory.FullName + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = GetDestination(root, entry);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = entry.Open();
                // Never restore archive attributes or create links; every output is a new regular file.
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                await source.CopyToAsync(output, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return scanner.Scan(workingDirectory.FullName);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }

    private static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            // Multipart file streams reject out-of-bounds seeks while ZIP headers are read.
            throw new InvalidDataException("Invalid ZIP archive offsets.", exception);
        }
    }

    private static string GetDestination(string root, ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains(':') ||
            name.Split('/').Any(segment => segment == ".." ||
                (segment != "." && (segment.EndsWith('.') || segment.EndsWith(' ')))) || unixType == 0xA000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Unsafe ZIP entry.");
        }

        string destination;
        try
        {
            destination = Path.GetFullPath(Path.Combine(root, name));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Invalid ZIP entry path.", exception);
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison))
        {
            throw new InvalidDataException("ZIP entry escapes the extraction directory.");
        }

        return destination;
    }
}
