namespace RepoInsight.Application;

public interface IRepositoryAnalysisService
{
    Task<RepositoryAnalysisResult> AnalyzeAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default);
}