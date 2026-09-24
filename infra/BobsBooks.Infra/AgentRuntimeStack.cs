using Amazon.CDK;
using Amazon.CDK.AWS.BedrockAgentCore;
using Amazon.CDK.AWS.Ecr.Assets;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Constructs;

namespace BobsBooks.Infra;

internal sealed class AgentRuntimeStack : Stack
{
    internal AgentRuntimeStack(
        Construct scope,
        string id,
        string gatewayUrl,
        string gatewayArn,
        string postfix,
        IStackProps props) : base(scope, id, props)
    {
        var runtimeName = $"{SampleConfiguration.RuntimeName}{postfix.Replace("-", "_")}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                runtimeName,
                "^[a-zA-Z][a-zA-Z0-9_]{0,47}$"))
        {
            throw new ArgumentException($"Runtime name '{runtimeName}' is invalid.", nameof(postfix));
        }

        var image = new DockerImageAsset(this, "AgentImage", new DockerImageAssetProps
        {
            Directory = Directory.GetCurrentDirectory(),
            File = "src/BobsBooks.Agent/Dockerfile",
            Platform = Platform_.LINUX_ARM64
        });
        var logGroup = new LogGroup(this, "RuntimeLogGroup", new LogGroupProps
        {
            LogGroupName = $"/aws/bedrock-agentcore/runtimes/{runtimeName}-DEFAULT",
            Retention = RetentionDays.ONE_WEEK,
            RemovalPolicy = RemovalPolicy.RETAIN
        });
        var runtimeArnPattern = FormatArn(new ArnComponents
        {
            Service = "bedrock-agentcore",
            Resource = "runtime",
            ResourceName = $"{runtimeName}*"
        });
        var role = new Role(this, "RuntimeRole", new RoleProps
        {
            AssumedBy = new ServicePrincipal(
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
                            ["aws:SourceArn"] = runtimeArnPattern
                        }
                    }
                }),
            Description = "Least-privilege execution role for the Bob's Books agent"
        });
        image.Repository.GrantPull(role);
        logGroup.GrantWrite(role);
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "InvokeAgentModel",
            Actions = ["bedrock:InvokeModel*"],
            Resources =
            [
                FormatArn(new ArnComponents
                {
                    Service = "bedrock",
                    Resource = "inference-profile",
                    ResourceName = SampleConfiguration.ModelId
                }),
                $"arn:{Partition}:bedrock:*::foundation-model/anthropic.claude-sonnet-4-6*"
            ]
        }));
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "InvokeSampleGateway",
            Actions = ["bedrock-agentcore:InvokeGateway"],
            Resources = [gatewayArn]
        }));
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "ExportTraces",
            Actions =
            [
                "xray:PutTraceSegments",
                "xray:PutTelemetryRecords",
                "xray:PutSpans",
                "xray:PutSpansForIndexing"
            ],
            Resources = ["*"]
        }));
        role.AddToPolicy(new PolicyStatement(new PolicyStatementProps
        {
            Sid = "PublishAgentCoreMetrics",
            Actions = ["cloudwatch:PutMetricData"],
            Resources = ["*"],
            Conditions = new Dictionary<string, object>
            {
                ["StringEquals"] = new Dictionary<string, object>
                {
                    ["cloudwatch:namespace"] = "bedrock-agentcore"
                }
            }
        }));

        var runtime = new CfnRuntime(this, "Runtime", new CfnRuntimeProps
        {
            AgentRuntimeName = runtimeName,
            Description = "Bob's Used Books Microsoft Agent Framework agent",
            RoleArn = role.RoleArn,
            AgentRuntimeArtifact = new CfnRuntime.AgentRuntimeArtifactProperty
            {
                ContainerConfiguration = new CfnRuntime.ContainerConfigurationProperty
                {
                    ContainerUri = image.ImageUri
                }
            },
            NetworkConfiguration = new CfnRuntime.NetworkConfigurationProperty
            {
                NetworkMode = "PUBLIC"
            },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["AGENTCORE_GATEWAY_URL"] = gatewayUrl,
                ["AGENT_OBSERVABILITY_ENABLED"] = "true",
                ["AWS_REGION"] = Region,
                ["BEDROCK_MODEL_ID"] = SampleConfiguration.ModelId,
                ["OTEL_BSP_EXPORT_TIMEOUT"] = "10000",
                ["OTEL_BSP_SCHEDULE_DELAY"] = "100",
                ["OTEL_DIAGNOSTIC_SPANS"] = "false",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = $"https://xray.{Region}.{UrlSuffix}/v1/traces",
                ["OTEL_EXPORTER_OTLP_TRACES_HEADERS"] =
                    $"x-aws-log-group={logGroup.LogGroupName},x-aws-log-stream=spans,x-aws-metric-namespace=bedrock-agentcore",
                ["OTEL_SERVICE_NAME"] = $"{runtimeName}.DEFAULT"
            }
        });

        _ = new CfnOutput(this, "RuntimeArn", new CfnOutputProps
        {
            Value = runtime.AttrAgentRuntimeArn
        });
    }
}
