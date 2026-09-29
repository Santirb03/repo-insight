using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace RepoInsight.Infrastructure;

public sealed class AzureOpenAINarrativeClient : INarrativeCompletionClient
{
    private readonly Lazy<ChatClient> client;

    public AzureOpenAINarrativeClient(AzureOpenAINarratorOptions options)
    {
        // Defer SDK construction so configuration/initialization errors occur inside the fallback boundary.
        client = new Lazy<ChatClient>(() => new AzureOpenAIClient(new Uri(options.Endpoint!),
            new ApiKeyCredential(options.ApiKey!), new AzureOpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(0)
            }).GetChatClient(options.Deployment!));
    }

    // Allows the real SDK request/response behavior to be tested with a mocked ChatClient.
    public AzureOpenAINarrativeClient(ChatClient client) => this.client = new Lazy<ChatClient>(() => client);

    public async Task<string> CompleteAsync(NarrativePrompt prompt, CancellationToken cancellationToken)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 1800,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("repository_narrative", BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {
                    "Summary": { "type": "string" },
                    "Strengths": { "type": "array", "items": { "type": "string" } },
                    "Risks": { "type": "array", "items": { "type": "string" } },
                    "Recommendations": { "type": "array", "items": { "type": "string" } }
                  },
                  "required": ["Summary", "Strengths", "Risks", "Recommendations"],
                  "additionalProperties": false
                }
                """), jsonSchemaIsStrict: true)
        };
        ChatCompletion completion = await client.Value.CompleteChatAsync(
            [new SystemChatMessage(prompt.Instructions), new UserChatMessage(prompt.UntrustedData)], options, cancellationToken);
        if (completion.FinishReason != ChatFinishReason.Stop || !string.IsNullOrEmpty(completion.Refusal) ||
            completion.Content.Count != 1 || completion.Content[0].Kind != ChatMessageContentPartKind.Text)
            throw new InvalidDataException("Narrative completion was refused, incomplete, or not text.");
        return completion.Content[0].Text;
    }
}
