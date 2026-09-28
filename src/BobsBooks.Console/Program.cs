using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Amazon.BedrockRuntime;
using BobsBooksConsole;
using BobsBooksShared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

const string instructions =
    "You are the Bob's Used Books assistant. Use the inventory tools and report only " +
    "titles, prices, quantities, and availability returned by those tools.";

// "--chat" starts an interactive chat. Otherwise prompts come from the command line; with no
// arguments the console runs the sample prompts.
var chat = args is ["--chat"];
string[] prompts = args.Length > 0
    ?
    [
        string.Join(' ', args)
    ]
    :
    [
        "What is book 34b1d665-3ef0-4921-ada0-94954b2e2b2f?",
        "Which hardcover mysteries do you have under $10?",
        "Do you have anything about serverless .NET?"
    ];

var region = Environment.GetEnvironmentVariable("AWS_REGION")
    ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
    ?? throw new InvalidOperationException("AWS_REGION is required.");

// With AGENTCORE_RUNTIME_ARN set, the console calls the agent deployed on AgentCore Runtime.
// Without it, the console runs the same agent in-process against the Gateway.
var runtimeArn = Environment.GetEnvironmentVariable("AGENTCORE_RUNTIME_ARN");
if (!string.IsNullOrWhiteSpace(runtimeArn))
{
    using var agentCore = new AmazonBedrockAgentCoreClient(RegionEndpoint.GetBySystemName(region));
    if (chat)
    {
        // One Runtime session for the whole chat; the history itself travels in every payload.
        var chatSessionId = Guid.NewGuid().ToString();
        Console.WriteLine($"RUNTIME_INVOKE arn={runtimeArn}");
        await ChatLoop.RunAsync(
            Console.In,
            Console.Out,
            (prompt, history) => InvokeRuntimeAsync(agentCore, runtimeArn, chatSessionId, prompt, history));
        return;
    }

    foreach (var prompt in prompts)
    {
        Console.WriteLine($"PROMPT {prompt}");
        Console.WriteLine($"RUNTIME_INVOKE arn={runtimeArn}");
        var answer = await InvokeRuntimeAsync(agentCore, runtimeArn, Guid.NewGuid().ToString(), prompt, []);
        Console.WriteLine($"FINAL_ANSWER {answer}");
        Console.WriteLine();
    }

    return;
}

var modelId = Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID")
    ?? throw new InvalidOperationException("BEDROCK_MODEL_ID is required.");

await using var catalog = new GatewayToolCatalog(new SigV4Handler());
var tools = await catalog.GetToolsAsync();
Console.WriteLine($"MCP_TOOLS count={tools.Count} names={string.Join(',', tools.Select(t => t.Name))}");

using var bedrock = new AmazonBedrockRuntimeClient(RegionEndpoint.GetBySystemName(region));
var innerAgent = new ChatClientAgent(
    bedrock.AsIChatClient(modelId),
    new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions { Instructions = instructions }
    });
var agent = innerAgent.AsBuilder()
    .Use(ToolLoggingMiddleware.Log)
    .Build();

if (chat)
{
    await ChatLoop.RunAsync(Console.In, Console.Out, AskAsync);
    return;
}

foreach (var prompt in prompts)
{
    Console.WriteLine($"PROMPT {prompt}");
    var response = await AskAsync(prompt, []);
    Console.WriteLine($"FINAL_ANSWER {response}");
    Console.WriteLine();
}

// Each prompt runs in a fresh session, with any earlier chat turns replayed before it.
async Task<string> AskAsync(string prompt, IReadOnlyList<ChatTurn> history)
{
    var session = await agent.CreateSessionAsync();
    var runOptions = new ChatClientAgentRunOptions(
        new ChatOptions { Tools = [.. tools] });
    var response = await agent.RunAsync(ChatTurn.ToMessages(history, prompt), session, runOptions);
    return response.Text;
}

static async Task<string> InvokeRuntimeAsync(
    IAmazonBedrockAgentCore client,
    string runtimeArn,
    string sessionId,
    string prompt,
    IReadOnlyList<ChatTurn> history)
{
    // A one-shot prompt sends only {"prompt":...}; a chat prompt also sends the earlier turns.
    object payload = history.Count == 0 ? new { prompt } : new { prompt, history };
    var request = new InvokeAgentRuntimeRequest
    {
        AgentRuntimeArn = runtimeArn,
        // Runtime session IDs must be at least 33 characters; a GUID string has 36.
        RuntimeSessionId = sessionId,
        ContentType = "application/json",
        Accept = "application/json",
        Payload = new MemoryStream(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web)))
    };

    using var response = await client.InvokeAgentRuntimeAsync(request);
    using var reader = new StreamReader(response.Response, Encoding.UTF8);
    var body = await reader.ReadToEndAsync();

    // The agent streams server-sent events: {"chunk":"..."} updates, then a final
    // {"message":"...","done":true} event with the complete answer.
    var chunks = new StringBuilder();
    foreach (var line in body.Split('\n'))
    {
        if (!line.StartsWith("data:", StringComparison.Ordinal))
        {
            continue;
        }

        using var document = JsonDocument.Parse(line["data:".Length..].Trim());
        if (document.RootElement.TryGetProperty("message", out var message))
        {
            return message.GetString() ?? string.Empty;
        }

        if (document.RootElement.TryGetProperty("chunk", out var chunk))
        {
            chunks.Append(chunk.GetString());
        }
    }

    return chunks.Length > 0 ? chunks.ToString() : body;
}
