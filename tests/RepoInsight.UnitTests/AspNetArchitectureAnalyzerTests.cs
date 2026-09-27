using System.Text.Json;
using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class AspNetArchitectureAnalyzerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-aspnet-").FullName;
    private readonly IArchitectureAnalyzer analyzer = new AspNetArchitectureAnalyzer();

    [Theory]
    [InlineData("OrdersController.cs", "[ApiController] public class OrdersController {}", "OrdersController", ArchitectureNodeType.Controller)]
    [InlineData("OrdersController.cs", "public class OrdersController : ControllerBase {}", "OrdersController", ArchitectureNodeType.Controller)]
    [InlineData("Pages.cs", "public class Pages : Microsoft.AspNetCore.Mvc.Controller {}", "Pages", ArchitectureNodeType.Controller)]
    [InlineData("Routes.cs", "[Microsoft.AspNetCore.Mvc.ApiControllerAttribute] public class Routes {}", "Routes", ArchitectureNodeType.Controller)]
    [InlineData("OrdersService.cs", "public class OrdersService : IOrdersService {}", "OrdersService", ArchitectureNodeType.Service)]
    [InlineData("OrdersService.cs", "public class OrdersService { public OrdersService(IOrdersRepository orders) {} }", "OrdersService", ArchitectureNodeType.Service)]
    [InlineData("OrdersService.cs", "public class OrdersService(ILogger<OrdersService> logger) {}", "OrdersService", ArchitectureNodeType.Service)]
    [InlineData("Services/Implementation.cs", "public class OrdersService { public OrdersService(AppDbContext db) {} }", "OrdersService", ArchitectureNodeType.Service)]
    [InlineData("OrdersRepository.cs", "public class OrdersRepository : IOrdersRepository {}", "OrdersRepository", ArchitectureNodeType.Repository)]
    [InlineData("OrdersRepository.cs", "public class OrdersRepository { public OrdersRepository(AppDbContext db) {} }", "OrdersRepository", ArchitectureNodeType.Repository)]
    [InlineData("OrdersRepository.cs", "public class OrdersRepository(AppDbContext db) {}", "OrdersRepository", ArchitectureNodeType.Repository)]
    [InlineData("AppDbContext.cs", "public class AppDbContext : DbContext { public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {} }", "AppDbContext", ArchitectureNodeType.DbContext)]
    [InlineData("Store.cs", "public class Store : global::Microsoft.EntityFrameworkCore.DbContext {}", "Store", ArchitectureNodeType.DbContext)]
    [InlineData("IdentityStore.cs", "public class IdentityStore : IdentityDbContext<AppUser> {}", "IdentityStore", ArchitectureNodeType.DbContext)]
    [InlineData("AuditMiddleware.cs", "public class AuditMiddleware : IMiddleware { public Task InvokeAsync(HttpContext context, RequestDelegate next) => next(context); }", "AuditMiddleware", ArchitectureNodeType.Middleware)]
    [InlineData("AuditMiddleware.cs", "public class AuditMiddleware { public AuditMiddleware(RequestDelegate next) {} public async Task InvokeAsync(HttpContext context) {} }", "AuditMiddleware", ArchitectureNodeType.Middleware)]
    [InlineData("AuditMiddleware.cs", "public class AuditMiddleware(RequestDelegate next) { public Task Invoke(HttpContext context) => next(context); }", "AuditMiddleware", ArchitectureNodeType.Middleware)]
    [InlineData("Worker.cs", "public class Worker : BackgroundService { protected override Task ExecuteAsync(CancellationToken token) => Task.CompletedTask; }", "Worker", ArchitectureNodeType.HostedService)]
    [InlineData("Worker.cs", "public class Worker : IHostedService {}", "Worker", ArchitectureNodeType.HostedService)]
    [InlineData("TokenHandler.cs", "public class TokenHandler : AuthenticationHandler<MyOptions> {}", "TokenHandler", ArchitectureNodeType.AuthenticationHandler)]
    [InlineData("TokenHandler.cs", "public class TokenHandler : IAuthenticationHandler {}", "TokenHandler", ArchitectureNodeType.AuthenticationHandler)]
    [InlineData("PermissionHandler.cs", "public class PermissionHandler : AuthorizationHandler<MyRequirement> {}", "PermissionHandler", ArchitectureNodeType.AuthorizationHandler)]
    [InlineData("PermissionHandler.cs", "public class PermissionHandler : IAuthorizationHandler {}", "PermissionHandler", ArchitectureNodeType.AuthorizationHandler)]
    public void DiscoversStructuredComponents(string path, string source, string name, ArchitectureNodeType type)
    {
        Write(path, source);
        var node = Assert.Single(Analyze().Nodes);
        Assert.Equal(name, node.DisplayName);
        Assert.Equal(type, node.NodeType);
        Assert.Equal(path, node.RelativeSourcePath);
        Assert.StartsWith("aspnet:", node.Id);
        Assert.False(Path.IsPathRooted(node.RelativeSourcePath));
    }

    [Theory]
    [InlineData("var builder = WebApplication.CreateBuilder(args); var app = builder.Build(); app.Run();")]
    [InlineData("var builder = Host.CreateApplicationBuilder(args); builder.Build().Run();")]
    [InlineData("namespace Example; public class Program { public static void Main(string[] args) { WebApplication.CreateBuilder(args).Build().Run(); } }")]
    [InlineData("namespace Example { public class Program { public static async Task Main(string[] args) {} } }")]
    [InlineData("public static class Program { public static int Main() => 0; }")]
    public void DiscoversApplicationEntryPoint(string source)
    {
        Write("Program.cs", source);
        var node = Assert.Single(Analyze().Nodes);
        Assert.Equal("Program", node.DisplayName);
        Assert.Equal(ArchitectureNodeType.Bootstrap, node.NodeType);
    }

    [Fact]
    public void MultipleProjects_GroupAndIdentifyComponentsIndependently()
    {
        Write("apps/store/Store.csproj", "<Project />");
        Write("apps/admin/Admin.csproj", "<Project />");
        Write("apps/store/Controllers/UsersController.cs", "namespace Store; public class UsersController : ControllerBase {}");
        Write("apps/admin/Controllers/UsersController.cs", "namespace Admin; public class UsersController : ControllerBase {}");
        Write("apps/store/Program.cs", "var app = WebApplication.CreateBuilder(args).Build(); app.Run();");
        Write("apps/admin/Program.cs", "var app = WebApplication.CreateBuilder(args).Build(); app.Run();");
        var nodes = Analyze().Nodes;
        Assert.Equal(4, nodes.Count);
        Assert.Equal(4, nodes.Select(node => node.Id).Distinct().Count());
        Assert.All(nodes.Where(node => node.RelativeSourcePath.StartsWith("apps/store/")), node => Assert.Equal("Store", node.ModuleName));
        Assert.All(nodes.Where(node => node.RelativeSourcePath.StartsWith("apps/admin/")), node => Assert.Equal("Admin", node.ModuleName));
    }

    [Fact]
    public void AmbiguousProjectGrouping_IsUnset()
    {
        Write("First.csproj", "<Project />");
        Write("Second.csproj", "<Project />");
        Write("AppController.cs", "public class AppController : ControllerBase {}");
        Assert.Null(Assert.Single(Analyze().Nodes).ModuleName);
    }

    [Fact]
    public void EmptyRepository_HasNoNodes() => Assert.Empty(Analyze().Nodes);

    [Theory]
    [InlineData("EmailService.cs", "")]
    [InlineData("EmailService.cs", "public interface EmailService {}")]
    [InlineData("EmailService.cs", "public record EmailService(string Value);")]
    [InlineData("EmailService.cs", "public struct EmailService {}")]
    [InlineData("EmailService.cs", "public class EmailService {}")]
    [InlineData("EmailService.cs", "public static class EmailService { public static void Send() {} }")]
    [InlineData("EmailService.cs", "public class EmailService : Exception { public EmailService(ILogger logger) {} }")]
    [InlineData("EmailRepository.cs", "public class EmailRepository : Attribute {}")]
    [InlineData("OrdersController.cs", "public class OrdersController { public string ControllerBase; }")]
    [InlineData("OrdersController.cs", "[NonController] public class OrdersController : ControllerBase {}")]
    [InlineData("AppDbContext.cs", "public class AppDbContext { public DbContext Context { get; set; } }")]
    [InlineData("AppDbContext.cs", "public class AppDbContext : List<DbContext> {}")]
    [InlineData("AuthHandler.cs", "public class AuthHandler {}")]
    [InlineData("Worker.cs", "public class Worker { public IHostedService Service { get; set; } }")]
    [InlineData("AuditMiddleware.cs", "public class AuditMiddleware { public string InvokeAsync(string value) => value; }")]
    [InlineData("Program.cs", "public class Program { public void Main() {} }")]
    [InlineData("Program.cs", "public class Helper { public void Setup() { WebApplication.CreateBuilder(); } }")]
    public void MisleadingNamesAndContradictoryStructures_AreNotComponents(string path, string source)
    {
        Write(path, source);
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void SpecificFrameworkRole_TakesPrecedenceOverServiceName()
    {
        Write("ReportService.cs", "public class ReportService : ControllerBase {}");
        Assert.Equal(ArchitectureNodeType.Controller, Assert.Single(Analyze().Nodes).NodeType);
    }

    [Theory]
    [InlineData("// [ApiController] public class FakeController : ControllerBase {}")]
    [InlineData("/* public class FakeContext : DbContext {} */")]
    [InlineData("const string example = \"public class FakeWorker : BackgroundService {}\";")]
    [InlineData("const string example = @\"[ApiController] public class FakeController {}\";")]
    [InlineData("var example = $\"[ApiController] public class FakeController {{}} {1}\";")]
    [InlineData("var example = $@\"public class Fake : IMiddleware {{}} {1}\";")]
    [InlineData("var example = \"\"\"public class Fake : DbContext {}\"\"\";")]
    [InlineData("var example = $$\"\"\"public class Fake : ControllerBase {} {{1}}\"\"\";")]
    [InlineData("var example = $\"{Format(\"[ApiController] public class Fake {}\")}\";")]
    [InlineData("var example = @\"escaped \"\" [ApiController] public class Fake {} \"\" quote\";")]
    [InlineData("const string example = \"WebApplication.CreateBuilder(args)\";")]
    public void CommentsAndStrings_DoNotCreateComponents(string source)
    {
        Write("Program.cs", source);
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void LiteralsInRealClass_DoNotChangeItsRole()
    {
        Write("EmailService.cs", """
            namespace App.Services;
            public class EmailService : IEmailService {
                public string Example = "[ApiController] class Fake : DbContext {}";
                // public class FakeWorker : BackgroundService {}
                public EmailService(ILogger<EmailService> logger) {}
            }
            """);
        Assert.Equal(ArchitectureNodeType.Service, Assert.Single(Analyze().Nodes).NodeType);
    }

    [Fact]
    public void NamespaceAndGenericArity_DistinguishClassesInOneFile()
    {
        Write("Controllers.cs", """
            namespace One { public class UsersController : ControllerBase {} }
            namespace Two {
                public class UsersController : ControllerBase {}
                public class UsersController<T> : ControllerBase {}
            }
            """);
        var nodes = Analyze().Nodes;
        Assert.Equal(3, nodes.Count);
        Assert.Equal(3, nodes.Select(node => node.Id).Distinct().Count());
    }

    [Fact]
    public void StableIdsAndOrdering_IgnoreDuplicatesScanOrderAndBodyChanges()
    {
        Write("src/EmailService.cs", "namespace App; public class EmailService : IEmailService {}");
        Write("src/UsersController.cs", "namespace App; public class UsersController : ControllerBase {}");
        var scan = new RepositoryScanner().Scan(root);
        var initial = analyzer.Analyze(root, scan);
        var duplicateScan = new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).Select(file => file with { RelativePath = file.RelativePath.Replace('/', '\\') }).ToArray());
        Write("src/EmailService.cs", "// changed body\nnamespace App; public class EmailService : IEmailService { public void Send() {} }");
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(analyzer.Analyze(root, duplicateScan)));
    }

    [Fact]
    public void IgnoredAndGeneratedFiles_DoNotContributeNodes()
    {
        foreach (var path in new[] { "bin/Fake.cs", "obj/Fake.cs", "Fake.g.cs", "Fake.generated.cs", "Fake.designer.cs" })
            Write(path, "public class Fake : ControllerBase {}");
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void LargeSource_IsNotPartiallyParsed()
    {
        Write("Fake.cs", "public class Fake : ControllerBase {}" + new string(' ', 1024 * 1024));
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void UnsafePathAndInvalidRoot_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(" ", new RepositoryScan([])));
        Assert.Throws<DirectoryNotFoundException>(() => analyzer.Analyze(Path.Combine(root, "missing"), new RepositoryScan([])));
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(root, new RepositoryScan([new RepositoryFile("../Fake.cs", ".cs", 1)])));
    }

    private ArchitectureGraph Analyze() => analyzer.Analyze(root, new RepositoryScanner().Scan(root));

    private void Write(string relativePath, string source)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
