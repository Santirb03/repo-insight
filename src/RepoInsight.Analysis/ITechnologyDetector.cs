using RepoInsight.Domain;

namespace RepoInsight.Analysis;

public interface ITechnologyDetector
{
    /// <summary>
    /// Detects technologies using scan metadata and selected local manifests. Call before deleting
    /// the extracted repository. Malformed or oversized manifests contribute filename evidence only.
    /// Content reads are limited to 512 KiB per selected file. Computed dependency expressions are
    /// not evaluated. File access errors propagate to the caller; unsafe paths and links are rejected.
    /// </summary>
    TechnologyProfile Detect(string repositoryPath, RepositoryScan scan);
}
