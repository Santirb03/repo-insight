using System.Text.Json;
using RepoInsight.Analysis;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

public sealed class NestJsArchitectureAnalyzerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-architecture-").FullName;
    private readonly IArchitectureAnalyzer analyzer = new NestJsArchitectureAnalyzer();

    [Theory]
    [InlineData("users.controller.ts", "@Controller('users') export class UsersController {}", "UsersController", ArchitectureNodeType.Controller)]
    [InlineData("users.service.ts", "@Injectable() export class UsersService {}", "UsersService", ArchitectureNodeType.Service)]
    [InlineData("users.module.ts", "@Module({ providers: [UsersService] }) export class UsersModule {}", "UsersModule", ArchitectureNodeType.Module)]
    [InlineData("auth.guard.ts", "@Injectable() export class AuthGuard implements CanActivate {}", "AuthGuard", ArchitectureNodeType.Guard)]
    [InlineData("jwt.strategy.ts", "@Injectable() export class JwtStrategy extends PassportStrategy(Strategy) {}", "JwtStrategy", ArchitectureNodeType.Strategy)]
    [InlineData("create-user.dto.ts", "export class CreateUserDto { @IsString() name: string; }", "CreateUserDto", ArchitectureNodeType.Dto)]
    [InlineData("user.dto.ts", "export class UserDTO {}", "UserDTO", ArchitectureNodeType.Dto)]
    [InlineData("prisma.service.ts", "@Injectable() export class PrismaService extends PrismaClient {}", "PrismaService", ArchitectureNodeType.DataAccessService)]
    [InlineData("database.ts", "@Injectable() export class Database extends PrismaClient {}", "Database", ArchitectureNodeType.DataAccessService)]
    [InlineData("provider.ts", "@Injectable() export class Clock {}", "Clock", ArchitectureNodeType.Service)]
    [InlineData("routes.ts", "@Controller('users') export class Users {}", "Users", ArchitectureNodeType.Controller)]
    [InlineData("feature.ts", "@Module({}) export class Feature {}", "Feature", ArchitectureNodeType.Module)]
    [InlineData("access.ts", "@Injectable() export class Access implements CanActivate {}", "Access", ArchitectureNodeType.Guard)]
    [InlineData("users.service.ts", "export class UsersService {}", "UsersService", ArchitectureNodeType.Service)]
    [InlineData("auth.guard.ts", "export class AuthGuard {}", "AuthGuard", ArchitectureNodeType.Guard)]
    [InlineData("jwt.strategy.ts", "export class JwtStrategy {}", "JwtStrategy", ArchitectureNodeType.Strategy)]
    public void DiscoversComponent(string path, string source, string name, ArchitectureNodeType type)
    {
        Write(path, source);
        var node = Assert.Single(Analyze().Nodes);
        Assert.Equal(name, node.DisplayName);
        Assert.Equal(type, node.NodeType);
        Assert.Equal(path, node.RelativeSourcePath);
        Assert.StartsWith("nestjs:", node.Id);
        Assert.False(Path.IsPathRooted(node.RelativeSourcePath));
    }

    [Theory]
    [InlineData("stripe.controller.ts", "@Controller('api/webhooks/stripe') export class StripeController {}")]
    [InlineData("webhooks.controller.ts", "@Controller('events') export class EventsController {}")]
    [InlineData("stripe.controller.ts", "@Controller({ path: ['events', 'webhook'], version: '1' }) export class StripeController {}")]
    [InlineData("stripe.controller.ts", "@Controller('stripe') export class StripeController { @Post('webhook') handle() {} }")]
    [InlineData("routes.ts", "@Controller() export class StripeWebhookController {}")]
    public void DistinguishesWebhookController(string path, string source)
    {
        Write(path, source);
        Assert.Equal(ArchitectureNodeType.WebhookController, Assert.Single(Analyze().Nodes).NodeType);
    }

    [Theory]
    [InlineData("@Controller({path: 'events', host: 'webhook.example'}) export class EventsController {}")]
    [InlineData("@Controller('notwebhook') export class EventsController {}")]
    [InlineData("@Controller('webhookish') export class EventsController {}")]
    [InlineData("@Controller('events') export class EventsController { text = 'webhook'; handle() { const s = '@Post(\"webhook\")'; } }")]
    public void UnrelatedWebhookText_DoesNotChangeControllerType(string source)
    {
        Write("events.controller.ts", source);
        Assert.Equal(ArchitectureNodeType.Controller, Assert.Single(Analyze().Nodes).NodeType);
    }

    [Fact]
    public void Monorepo_GroupsByNearestModuleAndKeepsSameNamedClassesDistinct()
    {
        Write("apps/api/src/users/users.module.ts", "@Module({}) export class UsersModule {}");
        Write("apps/api/src/users/users.service.ts", "@Injectable() export class UsersService {}");
        Write("apps/admin/src/users/users.module.ts", "@Module({}) export class AdminUsersModule {}");
        Write("apps/admin/src/users/users.service.ts", "@Injectable() export class UsersService {}");
        Write("apps/api/src/users/dto/create.dto.ts", "export class CreateDto {}");
        var nodes = Analyze().Nodes;
        Assert.Equal(5, nodes.Count);
        Assert.Equal(5, nodes.Select(node => node.Id).Distinct().Count());
        Assert.All(nodes.Where(node => node.RelativeSourcePath.StartsWith("apps/api/")), node => Assert.Equal("UsersModule", node.ModuleName));
        Assert.All(nodes.Where(node => node.RelativeSourcePath.StartsWith("apps/admin/")), node => Assert.Equal("AdminUsersModule", node.ModuleName));
    }

    [Fact]
    public void AmbiguousModuleGrouping_IsUnset()
    {
        Write("first.module.ts", "@Module({}) export class FirstModule {}");
        Write("second.module.ts", "@Module({}) export class SecondModule {}");
        Write("app.service.ts", "@Injectable() export class AppService {}");
        Assert.Null(Assert.Single(Analyze().Nodes, node => node.NodeType == ArchitectureNodeType.Service).ModuleName);
    }

    [Fact]
    public void StableIdsAndOrdering_IgnoreScanOrderDuplicatesAndBodyChanges()
    {
        Write("a.service.ts", "@Injectable() export class AService {}");
        Write("b.controller.ts", "@Controller('b') export class BController {}");
        var scan = new RepositoryScanner().Scan(root);
        var initial = analyzer.Analyze(root, scan);
        var repeated = new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).Select(file => file with { RelativePath = file.RelativePath.Replace('/', '\\') }).ToArray());
        Write("a.service.ts", "// added comment\n@Injectable() export class AService { count = 1; }");
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(analyzer.Analyze(root, repeated)));
    }

    [Fact]
    public void EmptyRepository_HasNoNodes() => Assert.Empty(Analyze().Nodes);

    [Theory]
    [InlineData("")]
    [InlineData("export const service = 1;")]
    [InlineData("export interface SomethingService {}")]
    [InlineData("export type SomethingService = { name: string };")]
    [InlineData("class UnrelatedHelper {}")]
    [InlineData("const value = class SomethingService {};")]
    [InlineData("function make() { @Injectable() class SomethingService {} }")]
    [InlineData("@Injectable() function something() {}")]
    [InlineData("export class SomethingService { method() {}")]
    public void MisleadingFilename_DoesNotInventService(string source)
    {
        Write("something.service.ts", source);
        Assert.Empty(Analyze().Nodes);
    }

    [Theory]
    [InlineData("// @Injectable() export class FakeService {}")]
    [InlineData("/* @Controller('webhook') export class FakeController {} */")]
    [InlineData("const example = '@Module({}) export class FakeModule {}';")]
    [InlineData("const example = \"@Injectable() export class FakeService {}\";")]
    [InlineData("const example = `@Injectable() export class FakeService {}`;")]
    [InlineData("const example = `outer ${`@Injectable() export class FakeService {}`} end`;")]
    [InlineData("const pattern = /@Injectable() class FakeService {}/;")]
    [InlineData("const pattern = () => /@Injectable() class FakeService {}/;")]
    public void CommentsAndLiterals_DoNotCreateComponents(string source)
    {
        Write("fake.service.ts", source);
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void DeeplyNestedTemplate_DoesNotExposeLiteralClassesOrOverflow()
    {
        var template = "`@Injectable() class FakeService {}`";
        for (var index = 0; index < 128; index++) template = "`outer ${" + template + "}`";
        Write("fake.service.ts", "const text = " + template + ";");
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void Decorators_BelongOnlyToTheirClassAndBodiesAreNotDeclarations()
    {
        Write("components.ts", """
            import { Controller, Injectable } from '@nestjs/common';
            @Controller({ path: 'users', description: '@Injectable() class FakeService {}' })
            export class UsersController {
                @Get() get() { class LocalService {} return '@Module({}) class FakeModule {}'; }
            }
            class Helper {}
            @Injectable()
            export abstract class Worker {}
            """);
        var nodes = Analyze().Nodes;
        Assert.Equal(new[] { "UsersController", "Worker" }, nodes.Select(node => node.DisplayName));
        Assert.Equal(new[] { ArchitectureNodeType.Controller, ArchitectureNodeType.Service }, nodes.Select(node => node.NodeType));
    }

    [Fact]
    public void TestDeclarationAndIgnoredFiles_AreExcluded()
    {
        foreach (var path in new[] { "fake.service.spec.ts", "fake.service.test.ts", "fake.d.ts", "node_modules/fake.service.ts", "dist/fake.service.ts" })
            Write(path, "@Injectable() export class FakeService {}");
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void LargeSource_IsNotPartiallyParsed()
    {
        Write("fake.service.ts", "@Injectable() export class FakeService {}" + new string(' ', 1024 * 1024));
        Assert.Empty(Analyze().Nodes);
    }

    [Fact]
    public void UnsafePathsAndInvalidRoot_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(" ", new RepositoryScan([])));
        Assert.Throws<DirectoryNotFoundException>(() => analyzer.Analyze(Path.Combine(root, "missing"), new RepositoryScan([])));
        Assert.Throws<ArgumentException>(() => analyzer.Analyze(root, new RepositoryScan([new RepositoryFile("../fake.service.ts", ".ts", 1)])));
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
