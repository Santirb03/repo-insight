using System.Text.Json;
using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class ArchitectureRelationshipTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-edges-").FullName;

    [Fact]
    public void NestJs_ConstructorChainAndModuleMetadata()
    {
        Write("auth.controller.ts", "import {AuthService} from './auth.service'; @Controller('auth') export class AuthController { constructor(private auth: AuthService) {} }");
        Write("auth.service.ts", "import {PrismaService} from './prisma.service'; @Injectable() export class AuthService { constructor(private prisma: PrismaService) {} }");
        Write("prisma.service.ts", "@Injectable() export class PrismaService extends PrismaClient {}");
        Write("database.module.ts", "@Module({}) export class DatabaseModule {}");
        Write("auth.module.ts", """
            import {AuthService} from './auth.service';
            import {AuthController} from './auth.controller';
            import {DatabaseModule} from './database.module';
            @Module({ imports: [DatabaseModule], providers: [AuthService], controllers: [AuthController] })
            export class AuthModule {}
            """);
        var graph = AnalyzeNest();
        Edge(graph, "AuthController", "AuthService", ArchitectureRelationshipType.Injects);
        Edge(graph, "AuthService", "PrismaService", ArchitectureRelationshipType.Injects);
        Edge(graph, "AuthService", "PrismaService", ArchitectureRelationshipType.UsesDatabase);
        Edge(graph, "AuthModule", "DatabaseModule", ArchitectureRelationshipType.Imports);
        Edge(graph, "AuthModule", "AuthService", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "AuthModule", "AuthController", ArchitectureRelationshipType.DependsOn);
        Assert.Equal(6, graph.Edges.Count);
        Validate(graph);
    }

    [Fact]
    public void AspNet_InterfaceChainAndRegistrationEvidence()
    {
        Write("App.csproj", "<Project />");
        Write("IUserService.cs", "namespace App; public interface IUserService {}");
        Write("UserService.cs", "namespace App; public class UserService(AppDbContext db) : IUserService {}");
        Write("AppDbContext.cs", "namespace App; public class AppDbContext : DbContext {}");
        Write("UsersController.cs", "namespace App; public class UsersController : ControllerBase { public UsersController(IUserService users) {} }");
        Write("Program.cs", "using App; var builder = WebApplication.CreateBuilder(args); builder.Services.AddScoped<IUserService, UserService>();");
        var graph = AnalyzeNet();
        Edge(graph, "UsersController", "IUserService", ArchitectureRelationshipType.Injects);
        var implementation = Edge(graph, "IUserService", "UserService", ArchitectureRelationshipType.Implements);
        Assert.Contains(implementation.Evidence, evidence => evidence.Contains("AddScoped<IUserService, UserService>() in Program.cs"));
        Assert.Contains(implementation.Evidence, evidence => evidence.Contains("UserService : IUserService in UserService.cs"));
        Edge(graph, "UserService", "AppDbContext", ArchitectureRelationshipType.Injects);
        Edge(graph, "UserService", "AppDbContext", ArchitectureRelationshipType.UsesDatabase);
        Assert.Equal(ArchitectureNodeType.Interface, Assert.Single(graph.Nodes, node => node.DisplayName == "IUserService").NodeType);
        Validate(graph);
    }

    [Theory]
    [InlineData("AddTransient")]
    [InlineData("AddSingleton")]
    [InlineData("AddScoped")]
    public void ServiceConfigurationMethods_PromoteExplicitlyRegisteredImplementations(string method)
    {
        Write("IClock.cs", "namespace App; public interface IClock {}");
        Write("Clock.cs", "namespace App; public class Clock : IClock {}");
        Write("ServiceSetup.cs", $"namespace App; public static class Setup {{ public static void Configure(IServiceCollection services) {{ services.{method}<IClock, Clock>(); }} }}");
        var graph = AnalyzeNet();
        var edge = Edge(graph, "IClock", "Clock", ArchitectureRelationshipType.Implements);
        Assert.Contains(edge.Evidence, evidence => evidence.StartsWith(method, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateRelationships_AggregateEvidenceAndOrderDeterministically()
    {
        Write("App.csproj", "<Project />");
        Write("IUserService.cs", "public interface IUserService {}");
        Write("UserService.cs", "public class UserService : IUserService {}");
        Write("UsersController.cs", "public class UsersController(IUserService first, IUserService second) : ControllerBase {}");
        Write("Program.cs", "var builder = WebApplication.CreateBuilder(args); builder.Services.AddScoped<IUserService, UserService>(); builder.Services.AddScoped<IUserService, UserService>();");
        var scan = new RepositoryScanner().Scan(root);
        var analyzer = new AspNetArchitectureAnalyzer();
        var expected = analyzer.Analyze(root, scan);
        var duplicated = new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).ToArray());
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(analyzer.Analyze(root, duplicated)));
        Assert.Equal(2, expected.Edges.Count);
        Validate(expected);
    }

    [Fact]
    public void NestJs_DuplicatesAreMerged()
    {
        Write("auth.service.ts", "@Injectable() export class AuthService {}");
        Write("auth.controller.ts", "@Controller() export class AuthController { constructor(private first: AuthService, private second: AuthService) {} }");
        Write("auth.module.ts", "@Module({ providers: [AuthService, AuthService] }) export class AuthModule {}");
        var scan = new RepositoryScanner().Scan(root);
        var analyzer = new NestJsArchitectureAnalyzer();
        var expected = analyzer.Analyze(root, scan);
        Assert.Equal(2, expected.Edges.Count);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(analyzer.Analyze(root, new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).ToArray()))));
        Validate(expected);
    }

    [Fact]
    public void NestJs_AmbiguousNamesAreSkippedButExplicitAliasedImportsResolve()
    {
        Write("first/user.service.ts", "@Injectable() export class UserService {}");
        Write("second/user.service.ts", "@Injectable() export class UserService {}");
        Write("ambiguous.controller.ts", "@Controller() export class AmbiguousController { constructor(private users: UserService) {} }");
        Write("explicit.controller.ts", "import {UserService as Users} from './first/user.service'; @Controller() export class ExplicitController { constructor(private users: Users) {} }");
        var graph = AnalyzeNest();
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("ExplicitController", graph.Nodes.Single(node => node.Id == edge.SourceNodeId).DisplayName);
        Assert.Equal("first/user.service.ts", graph.Nodes.Single(node => node.Id == edge.TargetNodeId).RelativeSourcePath);
    }

    [Fact]
    public void AspNet_AmbiguousNamespacesAreNotGuessed()
    {
        Write("a.cs", "namespace A; public interface IUserService {}");
        Write("b.cs", "namespace B; public interface IUserService {}");
        Write("UsersController.cs", "using A; using B; public class UsersController(IUserService users) : ControllerBase {}");
        Write("ExplicitController.cs", "public class ExplicitController(A.IUserService users) : ControllerBase {}");
        var graph = AnalyzeNet();
        var edge = Assert.Single(graph.Edges);
        Assert.Equal("ExplicitController", graph.Nodes.Single(node => node.Id == edge.SourceNodeId).DisplayName);
        Assert.Equal("a.cs", graph.Nodes.Single(node => node.Id == edge.TargetNodeId).RelativeSourcePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Monorepo_DependenciesStayInsideTheirProject(bool nest)
    {
        foreach (var project in new[] { "apps/a", "apps/b" })
        {
            if (nest)
            {
                Write(project + "/package.json", "{}");
                Write(project + "/user.service.ts", "@Injectable() export class UserService {}");
                Write(project + "/users.controller.ts", "@Controller() export class UsersController { constructor(private users: UserService) {} }");
            }
            else
            {
                Write(project + "/App.csproj", "<Project />");
                Write(project + "/UserService.cs", "public class UserService : IUserService {}");
                Write(project + "/UsersController.cs", "public class UsersController(UserService users) : ControllerBase {}");
            }
        }
        var graph = nest ? AnalyzeNest() : AnalyzeNet();
        Assert.Equal(2, graph.Edges.Count);
        foreach (var edge in graph.Edges)
        {
            var source = graph.Nodes.Single(node => node.Id == edge.SourceNodeId).RelativeSourcePath.Split('/')[1];
            var target = graph.Nodes.Single(node => node.Id == edge.TargetNodeId).RelativeSourcePath.Split('/')[1];
            Assert.Equal(source, target);
        }
    }

    [Fact]
    public void CommentsAndStrings_DoNotCreateInjectionOrRegistrations()
    {
        Write("user.service.ts", "@Injectable() export class UserService {}");
        Write("users.controller.ts", """
            @Controller() export class UsersController {
                // constructor(private users: UserService) {}
                text = "constructor(private users: UserService) {}";
                method() { const text = '@Module({providers:[UserService]})'; }
            }
            """);
        Write("IUserService.cs", "public interface IUserService {}");
        Write("UserService.cs", "public class UserService : IUserService {}");
        Write("UsersController.cs", """
            public class UsersController : ControllerBase {
                // public UsersController(IUserService users) {}
                string example = "public UsersController(IUserService users) {}";
            }
            """);
        Write("Program.cs", """
            // services.AddScoped<IUserService, UserService>();
            var text = "services.AddScoped<IUserService, UserService>();";
            """);
        Assert.Empty(AnalyzeNest().Edges);
        var graph = AnalyzeNet();
        Assert.DoesNotContain(graph.Edges, edge => edge.RelationshipType == ArchitectureRelationshipType.Injects);
        Assert.All(graph.Edges.SelectMany(edge => edge.Evidence), evidence => Assert.DoesNotContain("AddScoped", evidence));
    }

    [Fact]
    public void Stripe_RequiresImportAndActualUse()
    {
        Write("billing.service.ts", "import Stripe from 'stripe'; @Injectable() export class BillingService { constructor(private stripe: Stripe) {} }");
        Write("payments.service.ts", "import Stripe from 'stripe'; @Injectable() export class PaymentsService { client = new Stripe('key'); }");
        Write("unused.service.ts", "import Stripe from 'stripe'; @Injectable() export class UnusedService { example = 'new Stripe()'; }");
        var graph = AnalyzeNest();
        Edge(graph, "BillingService", "Stripe", ArchitectureRelationshipType.UsesExternalService);
        Edge(graph, "PaymentsService", "Stripe", ArchitectureRelationshipType.UsesExternalService);
        var unused = graph.Nodes.Single(node => node.DisplayName == "UnusedService");
        Assert.DoesNotContain(graph.Edges, edge => edge.SourceNodeId == unused.Id);
        Assert.Single(graph.Nodes, node => node.NodeType == ArchitectureNodeType.ExternalService);
    }

    [Fact]
    public void DynamicModuleEntriesAndStringInjectionTokensAreSkipped()
    {
        Write("user.service.ts", "@Injectable() export class UserService {}");
        Write("app.module.ts", "@Module({ providers: [{provide: 'token', useFactory: () => new UserService()}], imports: [forwardRef(() => DynamicModule)] }) export class AppModule {}");
        Write("users.controller.ts", "@Controller() export class UsersController { constructor(@Inject('TOKEN') users: UserService) {} }");
        Assert.Empty(AnalyzeNest().Edges);
    }

    [Fact]
    public void MultiplePublicConstructorsAreNotGuessed()
    {
        Write("IUserService.cs", "public interface IUserService {}");
        Write("UsersController.cs", "public class UsersController : ControllerBase { public UsersController() {} public UsersController(IUserService service) {} }");
        Assert.Empty(AnalyzeNet().Edges);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CrossProjectReferences_RequireExplicitProjectReference(bool reference)
    {
        Write("contracts/Contracts.csproj", "<Project />");
        Write("contracts/IUserService.cs", "namespace Contracts; public interface IUserService {}");
        Write("api/Api.csproj", reference
            ? "<Project><ItemGroup><ProjectReference Include='../contracts/Contracts.csproj' /></ItemGroup></Project>"
            : "<Project />");
        Write("api/UsersController.cs", "using Contracts; namespace Api; public class UsersController(IUserService users) : ControllerBase {}");
        var graph = AnalyzeNet();
        if (reference) Edge(graph, "UsersController", "IUserService", ArchitectureRelationshipType.Injects);
        else Assert.Empty(graph.Edges);
    }

    [Theory]
    [InlineData("import type {UserService} from './user.service';")]
    [InlineData("import {type UserService} from './user.service';")]
    public void TypeOnlyImports_DoNotImplyRuntimeInjection(string import)
    {
        Write("user.service.ts", "@Injectable() export class UserService {}");
        Write("users.controller.ts", import + " @Controller() export class UsersController { constructor(private users: UserService) {} }");
        Assert.Empty(AnalyzeNest().Edges);
    }

    [Fact]
    public void MalformedProjectMetadata_DoesNotCrashDiscovery()
    {
        Write("App.csproj", "<Project><Invalid");
        Write("IUserService.cs", "public interface IUserService {}");
        Write("UsersController.cs", "public class UsersController(IUserService users) : ControllerBase {}");
        Assert.Single(AnalyzeNet().Edges);
    }

    [Fact]
    public void AliasMustNotResolveToCoincidentalLocalInterface()
    {
        Write("IUserService.cs", "public interface IUserService {}");
        Write("UsersController.cs", "using IUserService = Missing.OtherService; public class UsersController(IUserService users) : ControllerBase {}");
        Assert.Empty(AnalyzeNet().Edges);
    }

    [Fact]
    public void MalformedFilesDoNotPreventValidRelationships()
    {
        Write("bad.service.ts", "@Injectable( export class BadService { constructor(");
        Write("BadController.cs", "public class BadController( : ControllerBase {");
        Write("auth.service.ts", "@Injectable() export class AuthService {}");
        Write("auth.controller.ts", "@Controller() export class AuthController { constructor(private auth: AuthService) {} }");
        Write("UsersController.cs", "public class UsersController(IUserService users) : ControllerBase {}");
        Write("IUserService.cs", "public interface IUserService {}");
        Assert.Single(AnalyzeNest().Edges);
        Assert.Single(AnalyzeNet().Edges);
    }

    [Fact]
    public void EmptyGraphsContainEmptyEdgeLists()
    {
        Assert.Empty(AnalyzeNest().Edges);
        Assert.Empty(AnalyzeNet().Edges);
        Assert.Empty(new ArchitectureGraph([]).Edges);
    }

    private static ArchitectureEdge Edge(ArchitectureGraph graph, string source, string target, ArchitectureRelationshipType type)
    {
        var sourceId = Assert.Single(graph.Nodes, node => node.DisplayName == source).Id;
        var targetId = Assert.Single(graph.Nodes, node => node.DisplayName == target).Id;
        return Assert.Single(graph.Edges, edge => edge.SourceNodeId == sourceId && edge.TargetNodeId == targetId && edge.RelationshipType == type);
    }

    private static void Validate(ArchitectureGraph graph)
    {
        Assert.Equal(graph.Edges.Count, graph.Edges.Select(edge => (edge.SourceNodeId, edge.TargetNodeId, edge.RelationshipType)).Distinct().Count());
        Assert.All(graph.Edges, edge =>
        {
            Assert.Contains(graph.Nodes, node => node.Id == edge.SourceNodeId);
            Assert.Contains(graph.Nodes, node => node.Id == edge.TargetNodeId);
            Assert.NotEmpty(edge.Evidence);
            Assert.All(edge.Evidence, evidence => Assert.False(string.IsNullOrWhiteSpace(evidence)));
            Assert.Equal(edge.Evidence.Count, edge.Evidence.Distinct().Count());
        });
    }

    private ArchitectureGraph AnalyzeNest() => new NestJsArchitectureAnalyzer().Analyze(root, new RepositoryScanner().Scan(root));
    private ArchitectureGraph AnalyzeNet() => new AspNetArchitectureAnalyzer().Analyze(root, new RepositoryScanner().Scan(root));

    private void Write(string path, string text)
    {
        var absolute = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, text);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
