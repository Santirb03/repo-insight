# Azure OpenAI repository narrator

The Azure narrator is an optional explanation layer behind `IRepositoryAnalysisNarrator`.
Technologies, architecture, diagnostics and Mermaid remain deterministic. The analysis service
and the `RepositoryNarrative` response contract are unchanged.

## Configuration

| Configuration key | PowerShell environment variable | Required/default |
| --- | --- | --- |
| `AzureOpenAI:Endpoint` | `AzureOpenAI__Endpoint` | HTTPS resource root, such as `https://RESOURCE.openai.azure.com/` |
| `AzureOpenAI:ApiKey` | `AzureOpenAI__ApiKey` | Azure resource API key |
| `AzureOpenAI:Deployment` | `AzureOpenAI__Deployment` | Your deployment name, not the base model name |
| `AzureOpenAI:TimeoutSeconds` | `AzureOpenAI__TimeoutSeconds` | Optional; 20 seconds, allowed range 1–120 |

Do not append `/openai/v1`, `/deployments`, or a query string to the endpoint: the Azure SDK
constructs the request path. Configuration is selected at startup; restart the API after changes.
No secrets belong in checked-in appsettings files.

## Azure Portal and Foundry steps

1. Sign in to [Azure Portal](https://portal.azure.com). Select **Create a resource**, search for
   **Azure OpenAI**, and select **Create**. Choose your subscription, resource group, resource
   name, region and the offered Standard pricing tier. You need permission to create resources
   and deploy models; model availability and quota depend on your subscription/region.
2. On **Networking**, choose access appropriate for your environment. For a local client with
   selected networks, allow its public outbound IP; a private endpoint requires corresponding
   VPN/VNet connectivity. Finish **Review + create**, then **Go to resource**.
3. In the resource's **Keys and Endpoint** page (under **Resource Management**), copy its endpoint
   and one API key into your local configuration. Keep the key out of source control and logs.
   This implementation uses API-key authentication; the resource must permit local/key authentication.
4. Open [Microsoft Foundry](https://ai.azure.com). For the documented classic navigation, turn
   **New Foundry** off, choose **View all resources**, and select the Azure OpenAI resource.
5. Choose **Shared resources → Deployments → + Deploy model → Deploy base model**. If the resource
   was upgraded to Foundry, the corresponding page is **My assets → Models + endpoints**.
6. Choose an available Chat Completions model supporting strict JSON-schema output, for example
   **gpt-4.1-mini**, version **2025-04-14**, if offered in your region. Name its deployment
   **repoinsight-narrator**, select an available interactive deployment type (Standard or Global
   Standard, not Batch), set appropriate TPM capacity, and select **Deploy**. Wait for **Succeeded**.
7. Set `AzureOpenAI__Deployment` to that exact deployment name. A model/deployment that does not
   support the requested schema will produce deterministic fallback, not a failed repository scan.

These steps follow Microsoft's [resource/deployment guide](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/create-resource)
and [structured output/model compatibility guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/structured-outputs).
Portal labels can differ between the new and classic Foundry experiences.

## Local PowerShell setup

Run from the repository root in the same shell that will launch the API. Replace the endpoint
placeholder; the key prompt hides your input and avoids putting the literal key in command history.

```powershell
$env:AzureOpenAI__Endpoint = 'https://YOUR-RESOURCE.openai.azure.com/'
$env:AzureOpenAI__Deployment = 'repoinsight-narrator'
$env:AzureOpenAI__TimeoutSeconds = '20'
$env:AzureOpenAI__ApiKey = [System.Net.NetworkCredential]::new('', (Read-Host 'Azure API key' -AsSecureString)).Password
dotnet run --project ./src/RepoInsight.Api/RepoInsight.Api.csproj
```

Submit a ZIP to the existing `POST /api/repositories/analyze` endpoint using multipart/form-data.
The `narrative` object still contains `summary`, `strengths`, `risks`, and `recommendations`.
The response deliberately does not add a new AI-provider/status field. No Azure resources are
provisioned by this application or by its tests.

To disable Azure locally, stop the API, remove the key, and restart it:

```powershell
Remove-Item Env:AzureOpenAI__ApiKey -ErrorAction SilentlyContinue
dotnet run --project ./src/RepoInsight.Api/RepoInsight.Api.csproj
```

This assumes no other configuration provider supplies the key. Environment variables above last
only for the current PowerShell process and its child processes.

## Fallback and cancellation

- Missing/invalid configuration registers `DeterministicRepositoryAnalysisNarrator` directly.
- Valid configuration registers `AzureOpenAIRepositoryAnalysisNarrator`, which calls an isolated SDK
  adapter and retains the deterministic narrator as fallback.
- SDK initialization errors, authentication/rate-limit/network failures, timeout, refusal, incomplete
  completion, invalid JSON, unexpected properties/types, and output-size violations all fall back.
- The timeout covers prompt construction and the asynchronous completion wait; retries are disabled.
  Cancellation initiated by the caller is propagated, rather than converted to a successful fallback.
- Exceptions, prompts, responses and API keys are not logged by this feature. The response schema is
  validated locally even though the request uses strict structured output.

## Data sent and limitations

Only bounded analysis metadata is supplied: at most 32 technologies, 40 nodes, 60 relationships,
20 findings, and 2 evidence entries per item. Findings with higher severity and their associated
nodes are prioritized. Individual fields are truncated, node IDs become short aliases, and the
serialized JSON data envelope is capped at 32,000 characters. Totals and omission counts are included.
The SDK output budget is 1,800 tokens; local validation permits a summary up to 2,000 characters and
up to 8 items of 1,000 characters per list, with a 20,000-character JSON ceiling.

The prompt builder cannot open repository files: its inputs are only `TechnologyProfile`,
`ArchitectureGraph` and diagnostic findings. Selected deterministic evidence and names/paths are
sent, so do not assume metadata is anonymous. Whole source files, file contents, ZIPs, configuration
secrets and environment variables are not attached. No tools are enabled and no URL or instruction
from repository data controls a request destination.

Trusted instructions are a separate system message; all repository-derived text is JSON-encoded
in a user message explicitly marked `UNTRUSTED_REPOSITORY_ANALYSIS_DATA`. The model is told to ignore
instructions embedded in that data, avoid unsupported claims, treat diagnostics as heuristics, and
avoid treating external-service usage as an inherent risk. Prompt isolation reduces injection risk;
it is not a proof of model compliance. Schema validation checks structure and bounds, not factual
truth. The deterministic analysis remains the source of truth.

## Dependencies and tests

One direct dependency was added to Infrastructure: **Azure.AI.OpenAI 2.1.0**, the stable Azure SDK
documented [here](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/ai.openai-readme?view=azure-dotnet).
It brings the official OpenAI .NET SDK and Azure/client dependencies transitively. This implementation
uses `AzureOpenAIClient.GetChatClient`, `CompleteChatAsync` and strict `CreateJsonSchemaFormat`.

```powershell
dotnet build RepoInsight.slnx
dotnet test RepoInsight.slnx
```

Tests use fake completion clients and an in-memory HTTP handler beneath the real SDK. They do not
read real Azure credentials, contact Azure, or provision services. A live deployment smoke test is
still required after you configure your own resource.
