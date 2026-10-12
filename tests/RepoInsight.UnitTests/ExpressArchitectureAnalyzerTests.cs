using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RepoInsight.Analysis;
using RepoInsight.Analysis.Mermaid;
using RepoInsight.Api;
using RepoInsight.Application;
using RepoInsight.Domain;

namespace RepoInsight.UnitTests;

// Existing upload tests audit process-wide temporary directories. Do not overlap their audit.
[CollectionDefinition("Express analysis", DisableParallelization = true)]
public sealed class ExpressAnalysisCollection { }

[Collection("Express analysis")]
public sealed class ExpressArchitectureAnalyzerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("RepoInsight-express-").FullName;
    private readonly ExpressArchitectureAnalyzer analyzer = new();
    public ExpressArchitectureAnalyzerTests() => Package("", "4");

    [Theory]
    [InlineData("4", "app.js", "import express from 'express'; const app = express(); app.get('/users/:id', (req,res) => res.json({})); app.listen(3000);")]
    [InlineData("5", "app.mjs", "import express from 'express'; const app = express(); app.post('/users', async (req,res) => { res.json({}); });")]
    [InlineData("5", "app.ts", "import express from 'express'; const app: Express = express(); app.get('/users/:id', (req: Request,res: Response) => res.json({}));")]
    [InlineData("4", "app.cjs", "const express = require('express'); const app = express(); app.get('/users/:id', (req,res) => res.json({})); module.exports = app;")]
    [InlineData("5", "app.cts", "const express = require('express'); const app = express(); app.get('/users/:id', (req,res) => res.json({}));")]
    public void DiscoversExpressInstancesAndRegisteredRoutes(string version, string path, string source)
    {
        Package("", version); Write(path, source);
        var graph = Analyze();
        var app = Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.ExpressApplication);
        var route = Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
        Assert.Equal("app", app.DisplayName);
        Assert.StartsWith(source.Contains(".post(", StringComparison.Ordinal) ? "POST /users" : "GET /users/:id", route.DisplayName);
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.RouteHandler);
        Assert.Contains(graph.Edges, e => e.SourceNodeId == app.Id && e.TargetNodeId == route.Id);
        Validate(graph);
    }

    [Theory]
    [InlineData("import {Router as R} from 'express'; const router = R();")]
    [InlineData("const {Router: R} = require('express'); const router = R();")]
    [InlineData("const R = require('express').Router; const router = R();")]
    [InlineData("import express from 'express'; const router = express.Router();")]
    public void RouterFactoriesRequireExpressBindings(string source)
    {
        Write("routes.ts", source + " router.get('/:id', (req,res) => res.send('ok')); ");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Router);
        Assert.Contains("router-local", Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void NestedRoutersComposeLiteralPrefixesWithEvidence()
    {
        Write("users.mts", "import {Router} from 'express'; export const users = Router(); users.get('/:id', (req,res) => res.send('ok'));");
        Write("api.ts", "import {Router} from 'express'; import {users} from './users.mts'; export const api = Router(); api.use('/users', users);");
        Write("app.ts", "import express from 'express'; import {api} from './api'; const app = express(); app.use('/api', api);");
        var graph = Analyze();
        var route = Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
        Assert.Equal("GET /api/users/:id", route.DisplayName);
        Edge(graph, "app", "api", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "api", "users", ArchitectureRelationshipType.DependsOn);
        Assert.Contains(graph.Edges.SelectMany(e => e.Evidence), e => e.Contains("mounts router at '/api'", StringComparison.Ordinal));
        Validate(graph);
    }

    [Fact]
    public void ChainedRoutesGlobalRouterAndRouteMiddlewareKeepRegistrationOrdering()
    {
        Write("app.ts", "import express from 'express'; import cors from 'cors'; const app = express(); const router = express.Router(); function auth(req,res,next) { next(); } function handle(req,res) { res.send('ok'); } app.use(express.json()); app.use(cors()); router.use(auth); router.route('/:id').get(auth,handle).post(handle); app.use('/api', router);");
        var graph = Analyze();
        Assert.Equal(new[] { "GET /api/:id", "POST /api/:id" }, graph.Nodes.Where(n => n.NodeType == ArchitectureNodeType.HttpRoute).Select(n => n.DisplayName).Order(StringComparer.Ordinal));
        Assert.Equal(ArchitectureNodeType.Middleware, Node(graph, "auth").NodeType);
        Assert.Equal(ArchitectureNodeType.RouteHandler, Node(graph, "handle").NodeType);
        Assert.Contains(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Middleware && n.DisplayName == "express.json");
        Assert.Contains(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Middleware && n.DisplayName == "cors");
        var edge = Edge(graph, "GET /api/:id", "auth", ArchitectureRelationshipType.DependsOn);
        Assert.Contains("argument 1", Assert.Single(edge.Evidence));
        Assert.Contains("argument 2", Assert.Single(Edge(graph, "GET /api/:id", "handle", ArchitectureRelationshipType.DependsOn).Evidence));
    }

    [Theory]
    [InlineData("get")]
    [InlineData("post")]
    [InlineData("put")]
    [InlineData("patch")]
    [InlineData("delete")]
    [InlineData("head")]
    [InlineData("options")]
    [InlineData("all")]
    public void SupportedHttpMethodsAreNotSettingsLookups(string method)
    {
        Write("app.js", "const express = require('express'); const app = express(); app.get('env'); app." + method + "('/x', (req,res) => res.send('x'));");
        Assert.Equal(method.ToUpperInvariant() + " /x", Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void CommonJsControllersServicesAndRepositoriesHaveRealCallDependencies()
    {
        Write("repositories/users.cjs", "function findUser() { return 1; } module.exports = {findUser};");
        Write("services/users.cjs", "const {findUser} = require('../repositories/users'); function getUser() { return findUser(); } exports.getUser = getUser;");
        Write("controllers/users.cjs", "const service = require('../services/users'); exports.listUsers = (req,res) => res.json(service.getUser());");
        Write("app.cjs", "const express = require('express'); const {listUsers} = require('./controllers/users'); const app = express(); app.get('/users', listUsers);");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.Controller, Node(graph, "listUsers").NodeType);
        Assert.Equal(ArchitectureNodeType.Service, Node(graph, "getUser").NodeType);
        Assert.Equal(ArchitectureNodeType.Repository, Node(graph, "findUser").NodeType);
        Edge(graph, "GET /users", "listUsers", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "listUsers", "getUser", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "getUser", "findUser", ArchitectureRelationshipType.DependsOn);
        Validate(graph);
    }

    [Fact]
    public void ErrorMiddlewareRequiresFourParameterImplementation()
    {
        Write("middleware/errors.ts", "export function errorHandler(err,req,res,next) { res.status(500).send('error'); } export function auth(req,res,next) { next(); }");
        Write("app.ts", "import express from 'express'; import {errorHandler,auth} from './middleware/errors'; const app = express(); app.use(auth); app.use(errorHandler);");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.ErrorMiddleware, Node(graph, "errorHandler").NodeType);
        Assert.Equal(ArchitectureNodeType.Middleware, Node(graph, "auth").NodeType);
        Assert.Empty(new ControllerDatabaseAccessRule().Evaluate(graph));
        Assert.Empty(new OrphanComponentRule().Evaluate(new ArchitectureGraph(graph.Nodes.Where(n => n.NodeType is ArchitectureNodeType.Router or ArchitectureNodeType.Middleware or ArchitectureNodeType.ErrorMiddleware).ToArray())));
    }

    [Theory]
    [InlineData("@prisma/client", "import {PrismaClient} from '@prisma/client'; export const db = new PrismaClient();", "db.user.findMany()")]
    [InlineData("mongoose", "import mongoose from 'mongoose'; export const db = mongoose.model('User', {});", "db.find()")]
    [InlineData("sequelize", "import {Sequelize} from 'sequelize'; export const db = new Sequelize('placeholder');", "db.query('select 1')")]
    [InlineData("typeorm", "import {DataSource} from 'typeorm'; export const db = new DataSource({});", "db.query('select 1')")]
    [InlineData("pg", "import {Pool} from 'pg'; export const db = new Pool();", "db.query('select 1')")]
    [InlineData("mysql2", "import mysql from 'mysql2'; export const db = mysql.createPool({});", "db.query('select 1')")]
    [InlineData("redis", "import {createClient} from 'redis'; export const db = createClient();", "db.get('key')")]
    public void ConstructsAndUsesDatabaseClientsWithDistinctEvidence(string package, string source, string query)
    {
        Write("db.ts", source);
        Write("repositories/users.ts", "import {db} from '../db'; export async function load() { return " + query + "; }");
        Write("app.ts", "import express from 'express'; import {load} from './repositories/users'; const app = express(); app.get('/users', async (req,res) => { res.json(await load()); });");
        var graph = Analyze();
        Assert.Equal(ArchitectureNodeType.DataAccessService, Node(graph, "db").NodeType);
        Edge(graph, "load", "db", ArchitectureRelationshipType.UsesDatabase);
        Assert.Contains(graph.Edges.SelectMany(e => e.Evidence), e => e.Contains("constructs client db from " + package, StringComparison.Ordinal));
    }

    [Fact]
    public void ImportOnlyAndConstructorOnlyDoNotClaimActualDatabaseUsage()
    {
        Write("db.ts", "import {PrismaClient} from '@prisma/client'; export const db = new PrismaClient();");
        var graph = Analyze();
        Assert.DoesNotContain(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesDatabase);
        Assert.Contains(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.Imports);
    }

    [Fact]
    public void ExternalClientsAndFetchOriginsDoNotExposeSecretsOrVisitUrls()
    {
        Write("client.ts", "import Stripe from 'stripe'; export const client = new Stripe('never-output-secret');");
        Write("services/pay.ts", "import {client} from '../client'; export async function pay() { client.charges.create({}); return fetch('https://api.example.test/path?token=never-output-secret'); }");
        var graph = Analyze();
        Edge(graph, "pay", "client", ArchitectureRelationshipType.UsesExternalService);
        Edge(graph, "pay", "https://api.example.test", ArchitectureRelationshipType.UsesExternalService);
        Assert.DoesNotContain("never-output-secret", JsonSerializer.Serialize(graph));
        Assert.Single(new ExternalServiceUsageRule().Evaluate(new ArchitectureGraph(graph.Nodes) { Edges = graph.Edges.Where(e => e.TargetNodeId == Node(graph, "client").Id && e.RelationshipType == ArchitectureRelationshipType.UsesExternalService).ToArray() }));
    }

    [Fact]
    public void PathAliasesAndExplicitReexportsResolveMountedRouters()
    {
        Write("tsconfig.json", "{\"compilerOptions\":{\"paths\":{\"@/*\":[\"src/*\"]}}}");
        Write("src/users.ts", "import {Router} from 'express'; export const users = Router(); users.get('/:id', (req,res) => res.send('ok'));");
        Write("src/index.ts", "export {users} from './users';");
        Write("app.ts", "import express from 'express'; import {users} from '@/index'; const app = express(); app.use('/users', users);");
        Assert.Equal("GET /users/:id", Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void AliasedExpressDependenciesAndLocallyReexportedFactoriesAreRecognized()
    {
        Write("package.json", "{\"dependencies\":{\"web-server\":\"npm:express@5\"}}");
        Write("factory.ts", "import engine from 'web-server'; export {engine as default};");
        Write("app.ts", "import express from './factory'; const app = express(); app.get('/ok', (req,res) => res.send('ok'));");
        var scan = Scan(); var tech = new TechnologyDetector().Detect(root, scan);
        Assert.Contains(tech.Technologies, t => t.Name == "Express");
        Assert.Equal("GET /ok", Assert.Single(analyzer.Analyze(root, scan).Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void StaticAppFactoriesAndSimpleAliasesRetainExpressOrigin()
    {
        Write("factory.ts", "import express from 'express'; export function createApp() { const local = express(); return local; }");
        Write("app.ts", "import {createApp} from './factory'; const app = createApp(); const alias = app; alias.get('/ok', (req,res) => res.send('ok'));");
        var graph = Analyze();
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.ExpressApplication);
        Assert.Equal("GET /ok", Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Theory]
    [InlineData("const app = {get() {}}; app.get('/fake', (req,res) => 1);")]
    [InlineData("const express = () => ({get(){}}); const app = express(); app.get('/fake', (req,res) => 1);")]
    [InlineData("// const app = express(); app.get('/fake', handler);\n const text = 'app.get(/fake, handler)';")]
    [InlineData("import express from 'express'; function configure(express) { const app = express(); app.get('/fake', (req,res) => 1); }")]
    [InlineData("import express from 'express'; const app = express(); function fake(app) { app.get('/fake', (req,res) => 1); }")]
    [InlineData("import type express from 'express'; const app = express(); app.get('/fake', (req,res) => 1);")]
    [InlineData("const require = () => (() => ({})); const express = require('express'); const app = express(); app.get('/fake', (req,res) => 1);")]
    [InlineData("import express from 'express'; let app = express(); app = unrelated; app.get('/fake', (req,res) => 1);")]
    public void NonExpressObjectsAndShadowedIdentifiersNeverProduceRoutes(string source)
    {
        Write("fake.ts", source);
        Assert.DoesNotContain(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
    }

    [Fact]
    public void DynamicMountsAndPathsRemainExplicitlyUnresolved()
    {
        Write("app.ts", "import express from 'express'; const app = express(); const router = express.Router(); router.get('/:id', (req,res) => res.send('ok')); app.use(prefixFromEnvironment, router); app.get(computePath(), (req,res) => 1);");
        var routes = Analyze().Nodes.Where(n => n.NodeType == ArchitectureNodeType.HttpRoute).ToArray();
        Assert.Contains(routes, n => n.DisplayName.Contains("router-local", StringComparison.Ordinal));
        Assert.Contains(routes, n => n.DisplayName.Contains("<dynamic path>", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, n => n.DisplayName == "GET /:id");
    }

    [Fact]
    public void MultipleMountsAreShownWithoutDuplicatingRouteDefinitions()
    {
        Write("app.ts", "import express from 'express'; const app = express(); const router = express.Router(); router.get('/x', (req,res) => 1); app.use('/a', router); app.use('/b', router);");
        Assert.Equal("GET /a/x | /b/x", Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void IndependentPackagesAndNestInternalsAreIsolated()
    {
        Package("apps/one", "4"); Package("apps/two", "5");
        Write("apps/nest/package.json", "{\"dependencies\":{\"@nestjs/core\":\"10\",\"@nestjs/platform-express\":\"10\"}}");
        Write("apps/react/package.json", "{\"dependencies\":{\"react\":\"19\"}}");
        foreach (var app in new[] { "one", "two", "nest", "react" }) Write($"apps/{app}/app.js", "import express from 'express'; const app = express(); app.get('/users', (req,res) => 1);");
        Write("apps/nest/auth.service.ts", "@Injectable() export class AuthService {}");
        Write("apps/react/Card.tsx", "export const Card = () => <div/>;");
        var graph = Analyze();
        Assert.Equal(2, graph.Nodes.Count(n => n.NodeType == ArchitectureNodeType.ExpressApplication));
        Assert.DoesNotContain(graph.Nodes, n => n.RelativeSourcePath.StartsWith("apps/nest/", StringComparison.Ordinal) || n.RelativeSourcePath.StartsWith("apps/react/", StringComparison.Ordinal));
        Assert.Single(new NestJsArchitectureAnalyzer().Analyze(root, Scan()).Nodes);
        Assert.Single(new ReactArchitectureAnalyzer().Analyze(root, Scan()).Nodes, n => n.NodeType == ArchitectureNodeType.Component);
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(n => n.Id).Distinct().Count());
    }

    [Fact]
    public void StaticallyDeclaredWorkspacePackagesCanSupplyServices()
    {
        Write("package.json", "{\"dependencies\":{\"express\":\"5\",\"@repo/users\":\"workspace:*\"}}");
        Write("packages/users/package.json", "{\"name\":\"@repo/users\",\"exports\":\"./services/users.ts\"}");
        Write("packages/users/services/users.ts", "export function listUsers() { return []; }");
        Write("app.ts", "import express from 'express'; import {listUsers} from '@repo/users'; const app = express(); app.get('/users', (req,res) => res.json(listUsers()));");
        var graph = Analyze();
        Assert.Contains(graph.Nodes, n => n.DisplayName == "listUsers" && n.NodeType == ArchitectureNodeType.Service);
        Assert.Contains(graph.Edges, e => e.TargetNodeId == Node(graph, "listUsers").Id && e.RelationshipType == ArchitectureRelationshipType.DependsOn);
    }

    [Fact]
    public void DeterministicOutputIgnoresScanDuplicatesAndTraversalOrder()
    {
        Write("app.ts", "import express from 'express'; const app = express(); function handler(req,res) { res.send('ok'); } app.get('/x', handler); app.get('/x', handler);");
        var scan = Scan(); var first = analyzer.Analyze(root, scan);
        var second = analyzer.Analyze(root, new RepositoryScan(scan.Files.Reverse().Concat(scan.Files).Select(f => f with { RelativePath = f.RelativePath.Replace('/', '\\') }).ToArray()));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Single(first.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
        Assert.Equal(first.Edges.Count, first.Edges.Select(e => (e.SourceNodeId, e.TargetNodeId, e.RelationshipType)).Distinct().Count());
    }

    [Fact]
    public void MalformedHugeGeneratedAndDeepSourcesAreSkipped()
    {
        Write("bad.ts", "const app = express( {");
        Write("huge.ts", new string(' ', 1024 * 1024 + 1));
        Write("deep.ts", new string('(', 129) + "1" + new string(')', 129));
        Write("tokens.ts", string.Concat(Enumerable.Repeat("value;", 60_000)));
        Write("node_modules/ghost/app.ts", "import express from 'express'; const ghost = express();");
        Write("app.test.ts", "import express from 'express'; const ghost = express();");
        Write("app.ts", "import express from 'express'; const app = express(); app.get('/ok', (req,res) => 1);");
        Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.ExpressApplication);
    }

    [Fact]
    public void CyclicRouterMountsAreBoundedAndMermaidPreservesArchitecture()
    {
        Write("app.ts", "import express from 'express'; const app = express(); const one = express.Router(); const two = express.Router(); one.use('/two', two); two.use('/one', one); app.use('/root', one); two.get('/x', (req,res) => 1);");
        var full = Analyze(); var simplified = new ArchitectureDiagramSimplifier().Simplify(full);
        Assert.Contains(simplified.Nodes, n => n.NodeType == ArchitectureNodeType.ExpressApplication);
        Assert.Contains(simplified.Nodes, n => n.NodeType == ArchitectureNodeType.Router);
        Assert.Contains(simplified.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
        var mermaid = new MermaidDiagramRenderer().Render(simplified);
        Assert.Contains("GET", mermaid); Assert.Contains("depends on", mermaid);
        Assert.True(full.Nodes.Count >= simplified.Nodes.Count);
    }

    [Fact]
    public void RouteHandlerDatabaseDiagnosticsDoNotApplyToMiddleware()
    {
        Write("db.ts", "import {PrismaClient} from '@prisma/client'; export const db = new PrismaClient();");
        Write("app.ts", "import express from 'express'; import {db} from './db'; const app = express(); app.use(async (req,res,next) => { await db.user.findMany(); next(); }); app.get('/users', async (req,res) => res.json(await db.user.findMany()));");
        var graph = Analyze();
        var finding = Assert.Single(new ControllerDatabaseAccessRule().Evaluate(graph));
        Assert.Equal("HTTP handler accesses database directly", finding.Title);
        Assert.Equal(ArchitectureNodeType.RouteHandler, graph.Nodes.Single(n => n.Id == finding.RelatedNodeIds[0]).NodeType);
    }

    [Fact]
    public void ShadowedHandlerAndTypedReassignedAppDoNotProduceFalseEdges()
    {
        Write("handler.ts", "export function handle(req,res) { res.send('ok'); }");
        Write("app.ts", "import express from 'express'; import {handle} from './handler'; const app = express(); function configure(handle) { app.get('/x', handle); } let bad: Express = express(); bad = unrelated; bad.get('/fake', handle);");
        var graph = Analyze();
        var route = Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute);
        Assert.Equal("GET /x", route.DisplayName);
        Assert.DoesNotContain(graph.Edges, e => e.SourceNodeId == route.Id);
    }

    [Fact]
    public void ModuleInitializationOperationsAreDifferentFromConstructingClients()
    {
        Write("startup.ts", "import mongoose from 'mongoose'; mongoose.connect('never-output-secret');");
        var graph = Analyze();
        Assert.Single(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesDatabase);
        Assert.DoesNotContain("never-output-secret", JsonSerializer.Serialize(graph));
    }

    [Fact]
    public void DefaultCommonJsDatabaseExportCanBeUsedByRepository()
    {
        Write("db.cjs", "const {Pool} = require('pg'); const db = new Pool(); module.exports = db;");
        Write("repositories/users.cjs", "const db = require('../db'); exports.load = () => db.query('select 1');");
        var graph = Analyze();
        Edge(graph, "load", "db", ArchitectureRelationshipType.UsesDatabase);
    }

    [Fact]
    public void ControllerClassesMustHaveAnActualRegisteredMethod()
    {
        Write("controllers/users.ts", "export class Users { list(req,res) { res.json([]); } }");
        Write("app.ts", "import express from 'express'; import {Users} from './controllers/users'; const controller = new Users(); const app = express(); app.get('/users', controller.list); app.get('/fake', controller.missing);");
        var graph = Analyze();
        Edge(graph, "GET /users", "Users", ArchitectureRelationshipType.DependsOn);
        var fake = Node(graph, "GET /fake");
        Assert.DoesNotContain(graph.Edges, e => e.SourceNodeId == fake.Id);
    }

    [Fact]
    public void OutputBudgetDoesNotProduceDanglingEdgesOrCrash()
    {
        var source = "import express from 'express'; const app = express(); function handle(req,res) { res.send('x'); } " +
            string.Join(' ', Enumerable.Range(0, 5000).Select(i => $"app.get('/{i}', handle);"));
        Write("app.ts", source);
        var graph = Analyze();
        Assert.Equal(5000, graph.Nodes.Count);
        Assert.True(graph.Edges.Count <= 20_000);
        Validate(graph);
    }

    [Fact]
    public void NestedAndBlockShadowingCannotCreateFakeExpressRoutes()
    {
        Write("app.ts", "import express from 'express'; const app = express(); app.get('/real', (req,res) => 1); { const app = other; app.get('/fake', (req,res) => 1); } app.use((app,res,next) => { app.get('/also-fake', handler); next(); });");
        Assert.Equal("GET /real", Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void ConciseArrowParametersAlsoShadowExpressObjects()
    {
        Write("app.ts", "import express from 'express'; const app = express(); const fake = app => app.get('/fake', handler); app.use(app => app.get('/also-fake', handler)); app.get('/real', (req,res) => 1);");
        Assert.Equal("GET /real", Assert.Single(Analyze().Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
    }

    [Fact]
    public void InlineIdsDoNotDependOnLineNumbersOrImplementationBody()
    {
        Write("app.ts", "import express from 'express'; const app = express(); app.use((req,res,next) => next()); app.get('/x', (req,res) => res.send('old'));");
        var first = Analyze();
        Write("app.ts", "\n\nimport express from 'express'; const app = express(); app.use((req,res,next) => next()); app.get('/x', (req,res) => res.send('new'));");
        Assert.Equal(first.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal), Analyze().Nodes.Select(n => n.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RepresentativeExpressZipRunsThroughFullPipelineAlongsideReactAndNest()
    {
        Package("apps/api", "5");
        Write("apps/api/controllers/users.ts", "export function list(req,res) { res.json([]); }");
        Write("apps/api/routes/users.ts", "import {Router} from 'express'; import {list} from '../controllers/users'; export const users = Router(); users.get('/:id', list);");
        Write("apps/api/app.ts", "import express from 'express'; import {users} from './routes/users'; const app = express(); app.use('/users', users); app.listen(3000);");
        Write("apps/web/package.json", "{\"dependencies\":{\"react\":\"19\"}}");
        Write("apps/web/Card.tsx", "export const Card = () => <div/>;");
        Write("apps/nest/package.json", "{\"dependencies\":{\"@nestjs/core\":\"10\"}}");
        Write("apps/nest/auth.service.ts", "@Injectable() export class AuthService {}");
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            { using var writer = new StreamWriter(archive.CreateEntry(Path.GetRelativePath(root, file).Replace('\\', '/')).Open()); writer.Write(File.ReadAllText(file)); }
        zip.Position = 0;
        using var services = new ServiceCollection().AddRepositoryScanning().BuildServiceProvider();
        var result = await services.GetRequiredService<IRepositoryAnalysisService>().AnalyzeAsync(zip);
        Assert.Single(result.Architecture.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute && n.DisplayName == "GET /users/:id");
        Assert.Single(result.Architecture.Nodes, n => n.NodeType == ArchitectureNodeType.Component);
        Assert.Contains(result.Architecture.Nodes, n => n.DisplayName == "AuthService");
        Assert.Contains("GET /users/:id", result.Mermaid);
        Assert.NotEmpty(result.Narrative.Summary);
        Validate(result.Architecture);
    }

    private ArchitectureGraph Analyze() => analyzer.Analyze(root, Scan());
    [Fact]
    public void NestedClosuresAndLibraryCallResultsAreNotProvenInstances()
    {
        Write("app.ts", "import express from 'express'; import axios from 'axios'; function fakeFactory() { const f = () => express(); } const unknown = wrapper(() => express()); const fake = fakeFactory(); fake.get('/fake', handler); unknown.get('/also-fake', handler); const result = axios.get('/status'); function handle(req,res) { result.json(); } const app = express(); app.get('/real', handle);");
        var graph = Analyze();
        Assert.Equal("GET /real", Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.HttpRoute).DisplayName);
        Assert.DoesNotContain(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesExternalService && e.SourceNodeId == Node(graph, "handle").Id);
    }

    [Fact]
    public void UnknownFactoriesContainingCallbacksAreNotInlineHandlers()
    {
        Write("app.ts", "import express from 'express'; const app = express(); app.get('/unknown', wrapper(() => 1)); app.get('/middleware', (req,res,next) => next());");
        var graph = Analyze();
        Assert.DoesNotContain(graph.Edges, e => e.SourceNodeId == Node(graph, "GET /unknown").Id);
        Assert.Single(graph.Nodes, n => n.NodeType == ArchitectureNodeType.Middleware);
        Assert.DoesNotContain(graph.Nodes, n => n.NodeType == ArchitectureNodeType.RouteHandler);
    }

    [Fact]
    public void NewlyDiscoveredHelpersAlsoHaveEvidenceBackedIntegrationCalls()
    {
        Write("helpers.ts", "import axios from 'axios'; export function request() { return axios.get('/status'); }");
        Write("app.ts", "import express from 'express'; import {request} from './helpers'; const app = express(); app.get('/status', (req,res) => request());");
        var graph = Analyze();
        Assert.Contains(graph.Edges, e => e.SourceNodeId == Node(graph, "request").Id && e.RelationshipType == ArchitectureRelationshipType.UsesExternalService);
        Validate(graph);
    }

    [Fact]
    public void LocalConstructedClientsRequireActualUnshadowedUse()
    {
        Write("services/users.ts", "import {PrismaClient} from '@prisma/client'; export function load() { const db = new PrismaClient(); db.user.findMany(); } export function unused() { const db = new PrismaClient(); } export function fake() { const db = new PrismaClient(); const run = db => db.query(); } export function reassigned() { let db = new PrismaClient(); db = other; db.query(); }");
        var graph = Analyze();
        Edge(graph, "load", "load.db", ArchitectureRelationshipType.DependsOn);
        Edge(graph, "load", "load.db", ArchitectureRelationshipType.UsesDatabase);
        Assert.Single(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesDatabase);
    }

    [Fact]
    public void DatabaseFactoriesAreConstructionRatherThanUsage()
    {
        Write("services/users.ts", "import mysql from 'mysql2'; export function create() { const pool = mysql.createPool({}); }");
        var graph = Analyze();
        Assert.Contains(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.DependsOn);
        Assert.DoesNotContain(graph.Edges, e => e.RelationshipType == ArchitectureRelationshipType.UsesDatabase);
    }

    [Fact]
    public void DependencyNamedExpressAliasingAnotherPackageDoesNotActivateAnalyzer()
    {
        Package("", "npm:koa@2");
        Write("app.ts", "import express from 'express'; const app = express(); app.get('/fake', handler);");
        Assert.DoesNotContain(new TechnologyDetector().Detect(root, Scan()).Technologies, t => t.Name == "Express");
        Assert.Empty(Analyze().Nodes);
    }

    private RepositoryScan Scan() => new RepositoryScanner().Scan(root);
    private static ArchitectureNode Node(ArchitectureGraph graph, string name) => Assert.Single(graph.Nodes, n => n.DisplayName == name && n.NodeType is not ArchitectureNodeType.Module and not ArchitectureNodeType.Bootstrap);
    private static ArchitectureEdge Edge(ArchitectureGraph graph, string source, string target, ArchitectureRelationshipType type) => Assert.Single(graph.Edges, e => e.SourceNodeId == Node(graph, source).Id && e.TargetNodeId == Node(graph, target).Id && e.RelationshipType == type);
    private static void Validate(ArchitectureGraph graph)
    {
        var ids = graph.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(graph.Edges, e => { Assert.NotEmpty(e.Evidence); Assert.Contains(e.SourceNodeId, ids); Assert.Contains(e.TargetNodeId, ids); });
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(n => n.Id).Distinct().Count());
    }
    private void Package(string directory, string version) => Write((directory.Length > 0 ? directory + "/" : "") + "package.json", JsonSerializer.Serialize(new { name = directory.Length > 0 ? directory : "fixture", dependencies = new Dictionary<string, string> { ["express"] = version } }));
    private void Write(string path, string source) { var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, source); }
    public void Dispose() => Directory.Delete(root, true);
}
