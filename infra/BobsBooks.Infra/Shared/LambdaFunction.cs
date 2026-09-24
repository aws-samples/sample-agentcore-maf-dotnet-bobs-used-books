using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Lambda.DotNet;
using Amazon.CDK.AWS.Logs;

namespace SharedConstructs;

using Amazon.CDK.AWS.Lambda.Destinations;
using Amazon.CDK.AWS.SQS;

using Constructs;

public class LambdaFunctionProps : FunctionProps
{
    public LambdaFunctionProps(string codePath)
    {
        CodePath = codePath;
    }
    public string CodePath { get; set; }
}

public class LambdaFunction : Construct
{
    public Function Function { get; }

    public LambdaFunction(
        Construct scope,
        string id,
        LambdaFunctionProps props) : base(
        scope,
        id)
    {
        this.Function = new DotNetFunction(
            this,
            id,
            new DotNetFunctionProps()
            {
                FunctionName = id,
                Runtime = Runtime.DOTNET_8,
                MemorySize = props.MemorySize ?? 1024,
                // An explicit log group (instead of the deprecated LogRetention custom resource)
                // is owned by the stack, so `cdk destroy` removes it.
                LogGroup = new LogGroup(this, $"{id}LogGroup", new LogGroupProps
                {
                    LogGroupName = $"/aws/lambda/{id}",
                    Retention = RetentionDays.ONE_DAY,
                    RemovalPolicy = Amazon.CDK.RemovalPolicy.DESTROY
                }),
                Handler = props.Handler,
                Environment = props.Environment,
                Tracing = Tracing.ACTIVE,
                ProjectDir = props.CodePath,
                SolutionDir = "./api",
                Bundling = new BundlingOptions
                {
                    CommandHooks = new CleanupGeneratedTemplates()
                },
                Architecture =
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                    System.Runtime.InteropServices.Architecture.Arm64
                        ? Architecture.ARM_64
                        : Architecture.X86_64,
                OnFailure = new SqsDestination(
                    new Queue(
                        this,
                        $"{id}FunctionDLQ")),
            });
    }
}


internal sealed class CleanupGeneratedTemplates : ICommandHooks
{
    public string[] BeforeBundling(string inputDir, string outputDir) => [];

    public string[] AfterBundling(string inputDir, string outputDir) =>
    [
        $"rm -f {inputDir}/BookInventory.Api/serverless.template " +
        $"{inputDir}/BookInventory.Authorization/serverless.template"
    ];
}
