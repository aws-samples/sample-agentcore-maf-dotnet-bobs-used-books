using BobsBooksShared;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")))
{
    Console.Error.WriteLine("Set AGENTCORE_GATEWAY_URL to the AgentCore Gateway MCP URL.");
    return 2;
}

await using var catalog = new GatewayToolCatalog(new SigV4Handler());
var tools = await catalog.GetToolsAsync();
Console.WriteLine($"MCP tools/list returned {tools.Count} tool(s):");
foreach (var tool in tools)
{
    Console.WriteLine($"- {tool.Name}: {tool.Description}");
}

return tools.Count == 2 ? 0 : 1;
