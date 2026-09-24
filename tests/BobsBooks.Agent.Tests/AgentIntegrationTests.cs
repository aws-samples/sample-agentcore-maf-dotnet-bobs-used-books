using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using AWS.AgentCore.Testing;

public sealed class AgentIntegrationTests
{
    [GatewayFact]
    public async Task HardcoverMysteries_UsesListBooksAndReturnsSeededTitles()
    {
        const int agentPort = 18080;
        var output = new ConcurrentQueue<string>();
        var agentProject = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/BobsBooks.Agent/BobsBooks.Agent.csproj"));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(
                "dotnet",
                $"run --project \"{agentProject}\" --no-build")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{agentPort}";
        process.StartInfo.Environment["AGENTCORE_GATEWAY_URL"] =
            Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")!;
        process.StartInfo.Environment["BEDROCK_MODEL_ID"] =
            Environment.GetEnvironmentVariable("BEDROCK_MODEL_ID")
                ?? "global.anthropic.claude-sonnet-4-6";
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            await WaitForAgentAsync(client, agentPort, process, output);

            var emulator = RuntimeEmulatorServer.Create($"http://127.0.0.1:{agentPort}", 0);
            await emulator.StartAsync();
            try
            {
                var emulatorUrl = emulator.Urls.Single();
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{emulatorUrl}/runtimes/local-agent/invocations");
                request.Headers.Add(
                    "X-Amzn-Bedrock-AgentCore-Runtime-Session-Id",
                    Guid.NewGuid().ToString());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(
                    "{\"prompt\":\"Which hardcover mysteries do you have under $10?\"}",
                    Encoding.UTF8,
                    "application/json");

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, body);
                Assert.Contains("Locked Room Ledger", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Midnight at Ashcroft", body, StringComparison.OrdinalIgnoreCase);

                await Task.Delay(500);
                Assert.Contains(
                    output,
                    line => line.Contains("TOOL_CALL", StringComparison.Ordinal) &&
                            line.Contains("ListBooks", StringComparison.Ordinal));
            }
            finally
            {
                await emulator.StopAsync();
                await emulator.DisposeAsync();
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            foreach (var line in output)
            {
                Console.WriteLine(line);
            }
        }
    }

    private static async Task WaitForAgentAsync(
        HttpClient client,
        int port,
        Process process,
        ConcurrentQueue<string> output)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Agent exited with {process.ExitCode}: {string.Join(Environment.NewLine, output)}");
            }

            try
            {
                using var response = await client.GetAsync($"http://127.0.0.1:{port}/ping");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("Agent did not become healthy within 60 seconds.");
    }
}


public sealed class GatewayFactAttribute : FactAttribute
{
    public GatewayFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AGENTCORE_GATEWAY_URL")))
        {
            Skip = "AGENTCORE_GATEWAY_URL is not set; live Gateway integration test skipped.";
        }
    }
}
