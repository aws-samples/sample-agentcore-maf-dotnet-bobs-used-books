using Amazon;
using Amazon.BedrockRuntime;
using BobsBooksShared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

const string instructions =
    "You are the Bob's Used Books assistant. Use the inventory tools and report only " +
    "titles, prices, quantities, and availability returned by those tools.";

var modelId = Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID")
    ?? throw new InvalidOperationException("BEDROCK_MODEL_ID is required.");

await using var catalog = new GatewayToolCatalog(new SigV4Handler());
var tools = await catalog.GetToolsAsync();
Console.WriteLine($"MCP_TOOLS count={tools.Count} names={string.Join(',', tools.Select(t => t.Name))}");

var region = Environment.GetEnvironmentVariable("AWS_REGION")
    ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
    ?? throw new InvalidOperationException("AWS_REGION is required.");
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

string[] prompts =
[
    "What is book 34b1d665-3ef0-4921-ada0-94954b2e2b2f?",
    "Which hardcover mysteries do you have under $10?",
    "Do you have anything about serverless .NET?"
];

foreach (var prompt in prompts)
{
    Console.WriteLine($"PROMPT {prompt}");
    var session = await agent.CreateSessionAsync();
    var runOptions = new ChatClientAgentRunOptions(
        new ChatOptions { Tools = [.. tools] });
    var response = await agent.RunAsync(prompt, session, runOptions);
    Console.WriteLine($"FINAL_ANSWER {response}");
    Console.WriteLine();
}
