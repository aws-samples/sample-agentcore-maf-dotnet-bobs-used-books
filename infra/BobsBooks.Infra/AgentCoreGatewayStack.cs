using System.Text.Json;
using Amazon.CDK;
using Amazon.CDK.AWS.BedrockAgentCore;
using Amazon.CDK.AWS.IAM;
using Constructs;

namespace BobsBooks.Infra;

internal sealed class AgentCoreGatewayStack : Stack
{
    public string GatewayArn { get; }
    public string GatewayUrl { get; }

    internal AgentCoreGatewayStack(
        Construct scope,
        string id,
        string restApiId,
        string stageName,
        string postfix,
        IStackProps props) : base(scope, id, props)
    {
        var gatewayName = $"{SampleConfiguration.GatewayNamePrefix}{postfix}";
        var gatewayArnPattern = FormatArn(new ArnComponents
        {
            Service = "bedrock-agentcore",
            Resource = "gateway",
            ResourceName = $"{gatewayName}*"
        });
        var principal = new ServicePrincipal(
            "bedrock-agentcore.amazonaws.com",
            new ServicePrincipalOpts
            {
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["aws:SourceAccount"] = Account
                    },
                    ["ArnLike"] = new Dictionary<string, object>
                    {
                        ["aws:SourceArn"] = gatewayArnPattern
                    }
                }
            });
        var role = new Role(this, "GatewayRole", new RoleProps
        {
            AssumedBy = principal,
            Description = "Invokes only the two Bob's Books read routes"
        });
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "InvokeBobsReadRoutes",
            Actions = ["execute-api:Invoke"],
            Resources =
            [
                FormatArn(new ArnComponents
                {
                    Service = "execute-api",
                    Resource = restApiId,
                    ResourceName = $"{stageName}/GET/books"
                }),
                FormatArn(new ArnComponents
                {
                    Service = "execute-api",
                    Resource = restApiId,
                    ResourceName = $"{stageName}/GET/books/*"
                })
            ]
        }));

        var gateway = new CfnGateway(this, "Gateway", new CfnGatewayProps
        {
            Name = gatewayName,
            Description = "MCP gateway for Bob's Used Books",
            ProtocolType = "MCP",
            AuthorizerType = "AWS_IAM",
            RoleArn = role.RoleArn
        });

        var targetFile = Path.Combine(
            Directory.GetCurrentDirectory(),
            "infra",
            "AgentCoreGateway",
            "target.json");
        var targetDefinition = JsonSerializer.Deserialize<TargetDefinition>(
            File.ReadAllText(targetFile),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not read {targetFile}.");

        _ = new CfnGatewayTarget(this, "ApiTarget", new CfnGatewayTargetProps
        {
            GatewayIdentifier = gateway.AttrGatewayIdentifier,
            Name = targetDefinition.Name,
            Description = targetDefinition.Description,
            CredentialProviderConfigurations = new object[]
            {
                new CfnGatewayTarget.CredentialProviderConfigurationProperty
                {
                    CredentialProviderType = "GATEWAY_IAM_ROLE"
                }
            },
            TargetConfiguration = new CfnGatewayTarget.TargetConfigurationProperty
            {
                Mcp = new CfnGatewayTarget.McpTargetConfigurationProperty
                {
                    ApiGateway = new CfnGatewayTarget.ApiGatewayTargetConfigurationProperty
                    {
                        RestApiId = restApiId,
                        Stage = stageName,
                        ApiGatewayToolConfiguration = new CfnGatewayTarget.ApiGatewayToolConfigurationProperty
                        {
                            ToolFilters = targetDefinition.ToolFilters.Select(filter =>
                                new CfnGatewayTarget.ApiGatewayToolFilterProperty
                                {
                                    FilterPath = filter.FilterPath,
                                    Methods = filter.Methods
                                }).ToArray(),
                            ToolOverrides = targetDefinition.ToolOverrides.Select(tool =>
                                new CfnGatewayTarget.ApiGatewayToolOverrideProperty
                                {
                                    Path = tool.Path,
                                    Method = tool.Method,
                                    Name = tool.Name,
                                    Description = tool.Description
                                }).ToArray()
                        }
                    }
                }
            }
        });

        GatewayArn = gateway.AttrGatewayArn;
        GatewayUrl = gateway.AttrGatewayUrl;
        _ = new CfnOutput(this, "GatewayUrl", new CfnOutputProps { Value = GatewayUrl });
        _ = new CfnOutput(this, "GatewayArn", new CfnOutputProps { Value = GatewayArn });
    }

    private sealed record TargetDefinition(
        string Name,
        string Description,
        ToolFilter[] ToolFilters,
        ToolOverride[] ToolOverrides);

    private sealed record ToolFilter(string FilterPath, string[] Methods);

    private sealed record ToolOverride(
        string Path,
        string Method,
        string Name,
        string Description);
}
