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

            await foreach (var update in agent.RunStreamingAsync(
                messages,
                session,
                runOptions,
                cancellationToken))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }
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

    [AgentCorePing]
    public object Ping() => new
    {
        status = "Healthy",
        time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    };
}
