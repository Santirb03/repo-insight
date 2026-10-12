# Deterministic Express architecture analysis

ExpressArchitectureAnalyzer implements the existing IArchitectureAnalyzer and is registered alongside NestJS, ASP.NET Core and React/Next.js. The analyze endpoint, ZIP protections, response fields and narrative selection are unchanged. No package was added.

## Strategy and activation

The analyzer reuses FrontendSyntax, FrontendSource, FrontendRepository and FrontendImportResolver. These are bounded structural parsers, not a JavaScript engine or complete semantic compiler. Comments, strings, templates, regex and JSX text do not become fake executable syntax. ExpressSource adds CommonJS bindings and lexical scope tracking; ExpressProgram resolves proven factories, instances, imports and simple aliases.

Activation requires an explicit Express dependency in the nearest package.json, including npm:express@ aliases. A dependency named express that aliases another package does not qualify. Nest's platform-express alone does not activate Express discovery. Dependencies may be in dependencies, devDependencies, peerDependencies or optionalDependencies.

Only files in those package scopes and statically resolved imports from them are analyzed. Names such as app, router or express are never proof by themselves. Explicit dependency origins are required. Shared source referenced by Express may contribute architecture without activating unrelated workspaces.

## Supported source patterns

- Express 4/5 application and Router factories, CommonJS and ESM; JavaScript and TypeScript.
- Default/named imports, simple namespace members, destructured require, require('express').Router, exported instances, explicit reexports, simple aliases and functions returning a proven application/router.
- Source extensions .js, .jsx, .ts, .tsx, .mjs, .cjs, .mts and .cts; extensionless and index resolution. Multiple candidates are rejected rather than guessed. Existing .js-to-.ts source mapping is retained.
- get, post, put, patch, delete, options, head and all registrations. Single-argument app.get settings lookups are excluded.
- app.route(path).get(handler).post(handler), router equivalents and nested app.use/router.use mounts.
- Literal route strings (including Express parameter syntax) and simple constants. The analyzer records paths; it does not compile or validate Express 4/5 path grammar.
- Inline and imported functions, declared controller class methods, layered services/repositories and known middleware factories.
- Relative imports, bounded tsconfig/jsconfig path aliases and relative configuration inheritance. Explicit workspace/file/link dependencies with unique package names and static exports/main entries can resolve shared packages.

Example: mounting users at /api/users, with users.get('/:id', authenticate, list), creates application → router → GET /api/users/:id → middleware/handler relationships. Each relationship has source evidence.

## Graph types and relationships

New additive node types: ExpressApplication, Router, HttpRoute, ErrorMiddleware. Existing types reused: Bootstrap, Module, Controller, RouteHandler, Middleware, Service, Repository, DataAccessService, ExternalService, ApiClient and Utility.

Services/repositories require actual function/class declarations in corresponding directory conventions, rather than arbitrary exported identifiers. Controllers require route registration evidence. A declared controller class member must actually exist. Middleware registration takes precedence over a controller-directory role; names such as authenticate do not prove authorization behavior.

Relationship semantics:

| Type | Express meaning |
| --- | --- |
| Imports | A resolved static import, at module level; not proof of invocation |
| DependsOn | Application construction, bootstrap, mount, route declaration, handler/middleware registration, concrete source function call, or client construction |
| UsesDatabase | An operation on a resolved database library/client |
| UsesExternalService | A resolved external SDK/HTTP-client call, or unshadowed fetch with a literal HTTP(S) origin |

No new relationship enum was introduced. Injects/Implements are not inferred for Express. Registration is not invocation: evidence explicitly says registers, while function implementation evidence says calls. Evidence includes relative paths, line numbers, registration ordinals and handler argument order. The model has no dedicated execution-order field, so consumers must not treat graph edge order as runtime execution order.

IDs hash normalized relative source path plus symbol/registration identity and use an express: prefix. Nodes and edges are deduplicated and sorted ordinally. Editing a function body or moving it between lines does not change its node ID; editing paths/names or anonymous registration order may do so. Evidence line numbers can change.

## Middleware and route composition

Global, router and route registrations are distinct evidence-backed edges. Known factories include cors, helmet, express-rate-limit, morgan, compression and body-parser. Built-in Express json, urlencoded, static, text and raw are recognized through a proven Express import. Four-parameter implementations become ErrorMiddleware; three-parameter implementations are treated conservatively as middleware. An unknown function name alone does not imply authentication or error behavior.

Literal mount prefixes compose across router chains. Multiple mounts retain one route-definition node with multiple composed paths. Cycles, dynamic prefixes and composition budgets preserve an unresolved marker. Unmounted routers are explicitly router-local. Dynamic expressions are never evaluated. Dynamic paths for the same owner and method currently share an unresolved route node with multiple evidence items: this is partial coverage rather than proof that their runtime paths match.

## Data and external integrations

Known database libraries: @prisma/client, sequelize, mongoose, typeorm, pg, mysql2 (including mysql2/promise), redis and ioredis. Known external clients: axios, stripe, @stripe/stripe-js, firebase-admin and @aws-sdk/client-s3.

Imports, construction and operations have separate evidence and relationships. Top-level and simple function-local constructed clients can be followed into operations. Reassigned or shadowed bindings are conservatively rejected. Client constructor arguments, connection strings and HTTP path/query strings are not copied into evidence. Literal fetch evidence records the origin only, rejects embedded credentials, and never visits the URL. This is not a general secret-redaction engine; route names and source identifiers remain visible in architecture evidence.

## Monorepos, diagnostics and Mermaid

Grouping uses the nearest package name and directory. Express in one workspace does not activate unrelated packages. React/Next.js and NestJS keep their own selection and scopes. Shared packages resolve only from explicit local dependency declarations; ambiguous names and conditional exports are not guessed.

The full graph keeps modules/imports/utilities. Mermaid simplifies to application, router, route, middleware/controller, service, repository, data and external clients, with an independent limit of 80 Express nodes. It omits trivial utilities/import edges and prioritizes application/router nodes. The existing renderer and relationship labels are reused. The React visual budget remains independent.

ARCH001 now also checks Express RouteHandler database access and uses an HTTP-handler-specific title. Routers/middleware are not treated as controllers for this rule. ARCH002, ARCH003 and TEST001 continue applying to actual Controller/Service roles; route-handler applicability is intentionally partial. ARCH004 retains informational evidence of external integrations. Existing NestJS/.NET diagnostic behavior is preserved.

## Safety and bounds

Repository files are read as text only, from the scanner's indexed paths. Ignored/generated/test sources are excluded; existing containment, symlink and ZIP checks remain active. No module loading, JS/TS execution, uploaded project scripts, npm installation, project binaries, network requests or expression evaluation occurs.

| Limit | Bound |
| --- | --- |
| Individual file / cumulative reads | 1 MiB / 32 MiB (including manifests/configs) |
| Attempted source files / aggregate parsed tokens | 10,000 / 1,000,000 |
| Tokens per file / syntax nesting | 100,000 / 128 |
| Function-body token analysis | 2,000,000 cumulative visits |
| Simple initializer/return expression inspection | 256 tokens per expression |
| Binding resolution / constant resolution | 16 / 8 hops |
| JSON depth / inherited configs | 64 / 8 levels |
| Workspace dependencies / static export entries | 256 / 128 per lookup/package |
| Registrations / mounts | 5,000 each |
| Composition depth / visits / parent mounts | 16 / 128 per route / 16 |
| Composed paths / path length | 8 per route / 1,024 characters |
| Full graph nodes / edges / evidence | 5,000 / 20,000 / 8 items per edge |
| Simplified Express diagram nodes | 80 |

Oversized, malformed, unbalanced or excessively nested source is safely skipped. Limits yield partial results, not an exception or an assertion of complete coverage. The API currently has no separate completeness flag for skipped files or output limits.

## Partial/unsupported patterns

- Dynamic require/import, computed members, dependency containers, decorators, runtime monkey-patching, eval and plugin-loaded routes.
- Arbitrary factories returning closures, callback arrays/spreads, complex nested object exports, conditionally constructed clients and advanced class/inheritance/type semantics.
- Conditional/wildcard workspace exports, arbitrary package-manager resolution and package-provided tsconfig inheritance.
- Evaluated template/concatenated/regex/array route paths; arbitrary Express path matching semantics.
- Full control-flow, reachability and runtime order: a statically evidenced registration inside a conditional is a possible declaration, not proof that it executes.
- Exhaustive SDK recognition or method-by-method database semantics; direct calls to recognized libraries are structural usage signals.
- Precise separation of every nested callback's behavior or middleware behavior based on its implementation.

Future improvements: dedicated registration/order metadata, distinct dynamic-route expression identities, completeness reporting, more static export/factory forms and broader integration fixtures. These can extend the existing abstractions without redesigning the API.

## Validation and manual procedure

ExpressArchitectureAnalyzerTests creates isolated temporary repositories and cleans them up. Its collection avoids overlapping the existing upload tests' process-wide temporary-directory audit. Tests assert concrete nodes, edges/evidence, deterministic output, malformed-source behavior, bounds, diagnostics and Mermaid compatibility.

The representative full-pipeline fixture is locally synthesized; no external open-source repository was downloaded. It ZIPs an Express API with mounted users router/controller, a React package and a NestJS package, then uses the real RepositoryAnalysisService and DI configuration. This exercises extraction, scanning, technology selection, graph merging, diagnostics, deterministic narrative and Mermaid without Azure credentials or network requests. It checks the composed route, framework coexistence, unique IDs and evidence-backed edges.

Manual validation:

1. Create package.json declaring express, app.js importing Express and a Router, and a users router registering GET /:id. Mount it at /api/users. Optionally add middleware and a service/repository/database call chain.
2. ZIP these files (exclude node_modules/build output). No installation or execution of that repository is necessary.
3. Run the trusted RepoInsight API: dotnet run --project src/RepoInsight.Api. Run the trusted web project with npm run dev from src/RepoInsight.Web, using NEXT_PUBLIC_API_BASE_URL for your API address.
4. Upload the ZIP to POST /api/repositories/analyze or through the UI. Check technologies includes Express; architecture includes the application/router and GET /api/users/:id; all relationships include evidence; Mermaid shows the meaningful chain.
5. Add a separate React/Nest workspace and confirm it retains its existing analyzer output. Add fake app.get text in comments/strings and a non-Express object: they should not become routes. Change a mount to a dynamic expression and confirm the unresolved label.

Quality gates: dotnet build RepoInsight.slnx, dotnet test RepoInsight.slnx; npm test, npm run lint and npm run build in the web project.
