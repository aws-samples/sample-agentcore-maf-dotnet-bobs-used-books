using Amazon.CDK;
using AuthenticationStack;
using BobsBooks.Infra;
using BookInventoryApiStack;

var app = new App();
var postfix = System.Environment.GetEnvironmentVariable("STACK_POSTFIX") ?? string.Empty;
var environment = new Amazon.CDK.Environment
{
    Account = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
    Region = System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
};

var auth = new AuthenticationStack.AuthenticationStack(
    app,
    $"BobsBooksAuth{postfix}",
    new AuthenticationProps(postfix),
    new StackProps { Env = environment });

var api = new BookInventoryServiceStack(
    app,
    $"BobsBooksApi{postfix}",
    new BookInventoryServiceStackProps(postfix),
    new StackProps { Env = environment });
api.AddDependency(auth);

var gateway = new AgentCoreGatewayStack(
    app,
    $"AgentCoreGateway{postfix}",
    api.RestApiId,
    api.StageName,
    postfix,
    new StackProps { Env = environment });

_ = new AgentRuntimeStack(
    app,
    $"AgentRuntime{postfix}",
    gateway.GatewayUrl,
    gateway.GatewayArn,
    postfix,
    new StackProps { Env = environment });

Tags.Of(app).Add("sample", SampleConfiguration.SampleTagValue);
app.Synth();
