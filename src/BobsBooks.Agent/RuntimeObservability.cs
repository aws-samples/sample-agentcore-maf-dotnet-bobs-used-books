using System.Diagnostics;
using AWS.AgentCore.Hosting;
using BobsBooksShared;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BobsBooksAgent;

internal static class RuntimeObservability
{
    private const string DefaultServiceName = "bobs_books_agent.DEFAULT";

    public static void AddRuntimeObservability(this WebApplicationBuilder builder)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? "us-east-1";
        var headers = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_TRACES_HEADERS");
        var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
            ?? DefaultServiceName;

        var diagnosticSpans = string.Equals(
            Environment.GetEnvironmentVariable("OTEL_DIAGNOSTIC_SPANS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"OTEL_PIPELINE endpoint={endpoint} service={serviceName}");

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAgentCoreInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(endpoint);
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.HttpClientFactory = () => new HttpClient(
                            SigV4Handler.CreateForService(region, "xray"));
                        if (!string.IsNullOrWhiteSpace(headers))
                        {
                            options.Headers = headers;
                        }
                    });
                if (diagnosticSpans)
                {
                    tracing.AddProcessor(new DiagnosticSpanProcessor());
                }
            });

        // AddOtlpExporter registers this named client; configure it afterwards so
        // the SigV4 primary handler wins the final IHttpClientFactory configuration.
        builder.Services.AddHttpClient("OtlpTraceExporter")
            .ConfigurePrimaryHttpMessageHandler(() =>
                SigV4Handler.CreateForService(region, "xray"));
    }
}

internal sealed class DiagnosticSpanProcessor : OpenTelemetry.BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        var tags = activity.TagObjects
            .Where(tag => tag.Key == "session.id" || tag.Key.StartsWith("gen_ai.", StringComparison.Ordinal))
            .Select(tag => $"{tag.Key}={tag.Value}");
        Console.WriteLine(
            $"OTEL_SPAN source={activity.Source.Name} name={activity.DisplayName} tags=[{string.Join(',', tags)}]");
    }
}
