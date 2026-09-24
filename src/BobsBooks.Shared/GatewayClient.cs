using Amazon;
using Amazon.Runtime;
using ModelContextProtocol.Client;

#pragma warning disable CS0618 // The evidence brief explicitly requires FallbackCredentialsFactory.

namespace BobsBooksShared;

public sealed class SigV4Handler : DelegatingHandler
{
    private readonly string _region;
    private readonly string _service;
    private readonly bool _configureMcpHeaders;

    public SigV4Handler()
        : this(
            Environment.GetEnvironmentVariable("AWS_REGION")
                ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
                ?? throw new InvalidOperationException("AWS_REGION is required."),
            "bedrock-agentcore",
            configureMcpHeaders: true)
    {
    }

    private SigV4Handler(
        string region,
        string service,
        bool configureMcpHeaders,
        HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler { AllowAutoRedirect = false })
    {
        _region = region;
        _service = service;
        _configureMcpHeaders = configureMcpHeaders;
    }

    public static SigV4Handler CreateForService(string region, string service) =>
        new(region, service, configureMcpHeaders: false);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var credentials = await FallbackCredentialsFactory.GetCredentials()
                .GetCredentialsAsync();
            SignRequest(request, payload, credentials);

            var response = await base.SendAsync(request, cancellationToken);
            LogExport(request, response);
            return response;
        }
        catch (Exception exception)
        {
            LogError(exception);
            throw;
        }
    }

    // The OpenTelemetry OTLP exporter (HttpProtobuf) on .NET 5+ sends through the
    // synchronous HttpClient.Send path. A DelegatingHandler that overrides only
    // SendAsync is bypassed there, so the export left unsigned and CloudWatch answered
    // "403 Missing Authentication Token". Both paths must sign.
    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = request.Content is null
                ? []
                : request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
            var credentials = FallbackCredentialsFactory.GetCredentials().GetCredentials();
            SignRequest(request, payload, credentials);

            var response = base.Send(request, cancellationToken);
            LogExport(request, response);
            return response;
        }
        catch (Exception exception)
        {
            LogError(exception);
            throw;
        }
    }

    private void LogExport(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (!_configureMcpHeaders)
        {
            var detail = string.Empty;
            if (!response.IsSuccessStatusCode && response.Content is not null)
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                detail = $" body={new string(body.Where(c => !char.IsControl(c)).Take(300).ToArray())}";
            }

            Console.WriteLine(
                $"OTLP_EXPORT endpoint={request.RequestUri!.GetLeftPart(UriPartial.Path)} status={(int)response.StatusCode}{detail}");
        }
    }

    private void LogError(Exception exception)
    {
        if (!_configureMcpHeaders)
        {
            Console.Error.WriteLine(
                $"OTLP_EXPORT_ERROR {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void SignRequest(
        HttpRequestMessage request,
        byte[] payload,
        ImmutableCredentials credentials)
    {
        if (_configureMcpHeaders)
        {
            request.Headers.Remove("MCP-Protocol-Version");
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-03-26");
        }

        if (_configureMcpHeaders && payload.Length > 0)
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("method", out var method))
            {
                request.Headers.Remove("Mcp-Method");
                request.Headers.TryAddWithoutValidation("Mcp-Method", method.GetString());
            }
        }

        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = now.ToString("yyyyMMdd");
        var payloadHash = Sha256Hex(payload);
        var host = request.RequestUri!.IsDefaultPort
            ? request.RequestUri.Host
            : request.RequestUri.Authority;

        request.Headers.Host = host;
        request.Headers.Remove("x-amz-date");
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (!string.IsNullOrEmpty(credentials.Token))
        {
            request.Headers.Remove("x-amz-security-token");
            request.Headers.TryAddWithoutValidation("x-amz-security-token", credentials.Token);
        }

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["content-type"] = request.Content?.Headers.ContentType?.ToString() ?? "application/json",
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate
        };
        if (_configureMcpHeaders)
        {
            headers["mcp-method"] = request.Headers.GetValues("Mcp-Method").Single();
            headers["mcp-protocol-version"] = "2025-03-26";
        }
        if (!string.IsNullOrEmpty(credentials.Token))
        {
            headers["x-amz-security-token"] = credentials.Token;
        }

        var canonicalHeaders = string.Concat(
            headers.Select(pair => $"{pair.Key}:{Normalize(pair.Value)}\n"));
        var signedHeaders = string.Join(';', headers.Keys);
        var canonicalRequest = string.Join('\n',
            request.Method.Method,
            request.RequestUri.AbsolutePath,
            request.RequestUri.Query.TrimStart('?'),
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var scope = $"{dateStamp}/{_region}/{_service}/aws4_request";
        var stringToSign = string.Join('\n',
            "AWS4-HMAC-SHA256",
            amzDate,
            scope,
            Sha256Hex(System.Text.Encoding.UTF8.GetBytes(canonicalRequest)));
        var signingKey = Sign(
            Sign(
                Sign(
                    Sign(System.Text.Encoding.UTF8.GetBytes("AWS4" + credentials.SecretKey), dateStamp),
                    _region),
                _service),
            "aws4_request");
        var signature = Convert.ToHexString(Sign(signingKey, stringToSign)).ToLowerInvariant();

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "AWS4-HMAC-SHA256",
                $"Credential={credentials.AccessKey}/{scope}, " +
                $"SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Sha256Hex(byte[] value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value))
            .ToLowerInvariant();

    private static byte[] Sign(byte[] key, string value) =>
        System.Security.Cryptography.HMACSHA256.HashData(
            key,
            System.Text.Encoding.UTF8.GetBytes(value));
}

public sealed class GatewayToolCatalog(SigV4Handler handler) : IAsyncDisposable
{
    private McpClient? _client;
    private IReadOnlyList<McpClientTool>? _tools;

    public async Task<IReadOnlyList<McpClientTool>> GetToolsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        var endpoint = Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")
            ?? throw new InvalidOperationException("AGENTCORE_GATEWAY_URL is required.");
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(endpoint) },
            httpClient);

        _client = await McpClient.CreateAsync(
            transport,
            cancellationToken: cancellationToken);
        _tools = (await _client.ListToolsAsync(
            cancellationToken: cancellationToken)).ToArray();

        return _tools;
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
