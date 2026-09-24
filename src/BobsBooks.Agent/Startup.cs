using AWS.AgentCore.Hosting;
using BobsBooksShared;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace BobsBooksAgent;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<SigV4Handler>();
        builder.Services.AddSingleton<GatewayToolCatalog>();
        builder.AddRuntimeObservability();

        builder.AddAgentCore(options =>
        {
            options.ModelId = Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID")
                ?? throw new InvalidOperationException("BEDROCK_MODEL_ID is required.");
            options.EnableSensitiveTelemetryData = true;
            options.AgentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new ChatOptions
                {
                    Instructions =
                        "You are the Bob's Used Books assistant. Use the inventory tools and report only " +
                        "titles, prices, quantities, and availability returned by those tools."
                }
            };
            options.ConfigureAgent = agent => agent.AsBuilder()
                .Use(ToolLoggingMiddleware.Log)
                .Build();
        });
    }
}
