# RepoInsight Web

Next.js / React / TypeScript client for the existing RepoInsight API. All displayed analysis comes from `POST /api/repositories/analyze`; there is no sample-data fallback or Azure-specific client code.

## Local development

From the repository root:

```powershell
dotnet run --project src/RepoInsight.Api --launch-profile http
```

In another terminal:

```powershell
cd src/RepoInsight.Web
npm ci
Copy-Item .env.example .env.local
npm run dev
```

Open http://localhost:3000. Node.js 22.16+ is recommended. `.env.local` sets `NEXT_PUBLIC_API_BASE_URL=http://localhost:5018`. This public variable contains only the API origin, never credentials. Next.js embeds it at build time; restart development or rebuild after changing it.

The API allows POST requests from `http://localhost:3000` **only in Development**. Use that exact browser origin. For production, configure an explicit allowed frontend origin in your deployment or a same-origin reverse proxy; the development policy does not enable production CORS. Serve both applications over HTTPS in production.

## Verification

```powershell
npm test
npm run lint
npm run build
npm start
```

Tests cover ZIP validation, multipart requests, response shape, API errors, cancellation, and severity mapping. No live API or Azure credentials are required for unit tests/build.

## Manual upload check

1. Start both applications and select or drop a ZIP containing a supported C# or NestJS repository (under 25 MiB).
2. Click **Analyze Repository**. Verify loading, real summary, technology evidence, diagnostics, counts and architecture diagram.
3. Expand evidence and Mermaid source. Check a narrow browser width and keyboard navigation.
4. Try a non-ZIP, an empty ZIP file, multiple dropped files, and a corrupt archive. Client validation checks name/size; archive validation remains the backend's responsibility.
5. Stop the API and retry to check the network error. Restart it and retry successfully. Cancel a running analysis to return to the upload form.

## Structure and safety

- `app/`: page, layout, responsive styles.
- `components/`: upload/workspace, dashboard sections and client-only Mermaid renderer.
- `lib/`: C#-aligned contracts, runtime response validation, HTTP client, presentation mappings and tests.
- Browser upload cap: 25 MiB (below default Kestrel request limits including multipart overhead).
- Mermaid uses strict rendering, rejects configuration directives and renders the returned SVG inside a sandboxed iframe with a restrictive CSP. No callbacks are bound. Diagram errors leave the report and source accessible.
- The browser never executes uploaded repository code. There is no authentication, persistence, or analysis history.
