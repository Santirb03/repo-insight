using RepoInsight.Analysis;
using RepoInsight.Analysis.Mermaid;
using RepoInsight.Api.Services;
using RepoInsight.Application;

namespace RepoInsight.Api;

public static class RepositoryEndpoints
{
    public static IServiceCollection AddRepositoryScanning(
        this IServiceCollection services)
    {
        services.AddScoped<IRepositoryScanner, RepositoryScanner>();

        services.AddScoped<IArchitectureAnalyzer, NestJsArchitectureAnalyzer>();
        services.AddScoped<IArchitectureAnalyzer, AspNetArchitectureAnalyzer>();

        services.AddScoped<ArchitectureAnalyzerSelector>();

        services.AddScoped<IMermaidDiagramRenderer, MermaidDiagramRenderer>();

        services.AddScoped<IArchitectureDiagnosticRule, ControllerDatabaseAccessRule>();
        services.AddScoped<IArchitectureDiagnosticRule, TooManyDependenciesRule>();
        services.AddScoped<ArchitectureDiagnosticsEngine>();

        services.AddScoped<RepositoryZipService>();

        services.AddScoped<IRepositoryAnalysisService, RepositoryAnalysisService>();

        services.AddScoped<ITechnologyDetector, TechnologyDetector>();

        services.AddScoped<ArchitectureDiagramSimplifier>();

        return services;
    }

    public static IEndpointRouteBuilder MapRepositoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/repositories/scan", ScanAsync);
        endpoints.MapPost("/api/repositories/analyze", AnalyzeAsync);

        return endpoints;
    }

    private static async Task<IResult> ScanAsync(
        HttpRequest request,
        RepositoryZipService zipService,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType ||
            !request.ContentType!.StartsWith(
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "Upload one .zip file using multipart/form-data."
            });
        }

        try
        {
            var form = await request.ReadFormAsync(cancellationToken);

            if (form.Files.Count != 1)
            {
                return Results.BadRequest(new
                {
                    error = "Exactly one .zip file is required."
                });
            }

            var file = form.Files[0];

            if (file.Length == 0 ||
                !Path.GetExtension(file.FileName)
                    .Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    error = "A non-empty .zip file is required."
                });
            }

            await using var stream = file.OpenReadStream();

            var result = await zipService.ScanAsync(
                stream,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new
            {
                error =
                    "The ZIP archive or multipart form is invalid or contains an unsafe entry."
            });
        }
    }

    private static async Task<IResult> AnalyzeAsync(
        HttpRequest request,
        IRepositoryAnalysisService analysisService,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType ||
            !request.ContentType!.StartsWith(
                "multipart/form-data",
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "Upload one .zip file using multipart/form-data."
            });
        }

        try
        {
            var form = await request.ReadFormAsync(cancellationToken);

            if (form.Files.Count != 1)
            {
                return Results.BadRequest(new
                {
                    error = "Exactly one .zip file is required."
                });
            }

            var file = form.Files[0];

            if (file.Length == 0 ||
                !Path.GetExtension(file.FileName)
                    .Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new
                {
                    error = "A non-empty .zip file is required."
                });
            }

            await using var stream = file.OpenReadStream();

            var result = await analysisService.AnalyzeAsync(
                stream,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new
            {
                error =
                    "The ZIP archive or multipart form is invalid or contains an unsafe entry."
            });
        }
    }
}