#pragma warning disable ASPIREAWSAGENTCORE001

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAgentCoreRuntime<Projects.BobsBooks_Agent>("agent")
    .WithAgentCoreStreaming()
    .WithEnvironment(
        "AGENTCORE_GATEWAY_URL",
        builder.Configuration["AGENTCORE_GATEWAY_URL"]
            ?? throw new InvalidOperationException("AGENTCORE_GATEWAY_URL is required."))
    .WithEnvironment(
        "BEDROCK_MODEL_ID",
        builder.Configuration["BEDROCK_MODEL_ID"]
            ?? throw new InvalidOperationException("BEDROCK_MODEL_ID is required."));

builder.Build().Run();
