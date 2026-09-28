using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTelemetry;
using AWS.AgentCore.Hosting;
using BobsBooksShared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BobsBooksAgent;

public sealed class Agent(AIAgent agent, GatewayToolCatalog catalog)
{
    [AgentCoreHandler]
    public async IAsyncEnumerable<string> Handle(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Validate the client's chat history before any Gateway or model call.
        var messages = request.ToMessages();

        var previousSessionId = Baggage.GetBaggage("session.id");
        Baggage.SetBaggage("session.id", context.SessionId);
        Activity.Current?.SetTag("session.id", context.SessionId);
        Activity.Current?.SetTag("gen_ai.operation.name", "invoke_agent");
        Activity.Current?.SetTag(
            "gen_ai.agent.name",
            Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") ?? "bobs_books_agent.DEFAULT");

        try
        {
            var tools = await catalog.GetToolsAsync(cancellationToken);
            var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
            var runOptions = new ChatClientAgentRunOptions(
                new ChatOptions { Tools = [.. tools] });

            await foreach (var text in AnswerText(agent.RunStreamingAsync(
                messages,
                session,
                runOptions,
                cancellationToken)))
            {
                yield return text;
            }
        }
        finally
        {
            if (previousSessionId is null)
            {
                Baggage.RemoveBaggage("session.id");
            }
            else
            {
                Baggage.SetBaggage("session.id", previousSessionId);
            }
        }
    }

    // Streams the answer text. When a question needs a tool, the model answers in two
    // messages: a short one with the tool call, then the answer once the tool result is
    // back. The answer starts on a new line so the two don't run together
    // ("...for you!Great news...").
    public static async IAsyncEnumerable<string> AnswerText(
        IAsyncEnumerable<AgentResponseUpdate> updates)
    {
        var lastText = string.Empty;
        var afterToolResult = false;
        await foreach (var update in updates)
        {
            if (update.Contents.Any(content => content is FunctionResultContent))
            {
                afterToolResult = true;
            }

            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            if (afterToolResult && lastText.Length > 0 && !lastText.EndsWith('\n'))
            {
                yield return "\n";
            }

            afterToolResult = false;
            lastText = update.Text;
            yield return update.Text;
        }
    }

    [AgentCorePing]
    public object Ping() => new
    {
        status = "Healthy",
        time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
}
