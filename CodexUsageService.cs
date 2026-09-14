using System.Diagnostics;
using System.Text.Json;

namespace CodexUsageWidget;

internal sealed class CodexUsageService : IDisposable
{
    private const int FiveHourMinutes = 300;
    private const int WeeklyMinutes = 10080;

    private CodexAppServerClient? _client;
    private bool _disposed;

    public async Task<UsageSnapshot> GetUsageAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CodexUsageService));

        try
        {
            return await ReadUsageAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write("Usage read failed; restarting Codex app-server", ex);
            ResetClient();
            return await ReadUsageAsync();
        }
    }

    private async Task<UsageSnapshot> ReadUsageAsync()
    {
        _client ??= await CodexAppServerClient.StartAndInitializeAsync();

        using JsonDocument response = await _client.ReadRateLimitsAsync();

        List<UsageWindow> windows = FindWindows(response.RootElement)
            .DistinctBy(w => (w.WindowDurationMinutes, w.UsedPercent, w.ResetsAt))
            .ToList();

        UsageWindow? fiveHour = windows
            .Where(w => w.WindowDurationMinutes == FiveHourMinutes)
            .OrderByDescending(w => w.UsedPercent)
            .FirstOrDefault();

        UsageWindow? weekly = windows
            .Where(w => w.WindowDurationMinutes == WeeklyMinutes)
            .OrderByDescending(w => w.UsedPercent)
            .FirstOrDefault();

        return new UsageSnapshot(
            fiveHour,
            weekly,
            DateTimeOffset.Now);
    }

    private static IEnumerable<UsageWindow> FindWindows(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("windowDurationMins", out var durationNode) &&
                durationNode.ValueKind == JsonValueKind.Number &&
                durationNode.TryGetInt32(out int duration) &&
                element.TryGetProperty("usedPercent", out var usedNode) &&
                usedNode.ValueKind == JsonValueKind.Number &&
                usedNode.TryGetDouble(out double used))
            {
                DateTimeOffset? resetAt = null;

                if (element.TryGetProperty("resetsAt", out var resetNode) &&
                    resetNode.ValueKind == JsonValueKind.Number &&
                    resetNode.TryGetInt64(out long unix))
                {
                    try
                    {
                        resetAt =
                            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime();
                    }
                    catch
                    {
                    }
                }

                yield return new UsageWindow(
                    duration,
                    Math.Clamp(used, 0d, 100d),
                    resetAt);
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                foreach (UsageWindow window in FindWindows(property.Value))
                    yield return window;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (UsageWindow window in FindWindows(item))
                    yield return window;
            }
        }
    }

    private void ResetClient()
    {
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ResetClient();
    }
}

internal sealed class CodexAppServerClient : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly CancellationTokenSource _lifetime = new();

    private int _requestId;
    private bool _disposed;

    private CodexAppServerClient(Process process)
    {
        _process = process;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;
    }

    public static async Task<CodexAppServerClient> StartAndInitializeAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /s /c \"codex app-server --stdio\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException(
                    "Failed to start Codex app-server.");
        }
        catch (Exception ex)
        {
            process.Dispose();

            throw new InvalidOperationException(
                "Could not start Codex CLI. Confirm `codex --version` works in PowerShell.",
                ex);
        }

        var client = new CodexAppServerClient(process);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardError.EndOfStream)
                {
                    string? line =
                        await process.StandardError.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(line))
                        AppLog.Write("[codex stderr] " + line);
                }
            }
            catch
            {
            }
        });

        try
        {
            await client.InitializeAsync();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task InitializeAsync()
    {
        int id = NextId();

        await SendAsync(new
        {
            method = "initialize",
            id,
            @params = new
            {
                clientInfo = new
                {
                    name = "codex_usage_widget",
                    title = "Codex Usage Widget",
                    version = "0.3.0"
                }
            }
        });

        using JsonDocument response =
            await ReadForIdAsync(id, TimeSpan.FromSeconds(15));

        ThrowIfError(response, "Codex initialization failed");

        await SendAsync(new
        {
            method = "initialized",
            @params = new { }
        });
    }

    public async Task<JsonDocument> ReadRateLimitsAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CodexAppServerClient));

        int id = NextId();

        await SendAsync(new
        {
            method = "account/rateLimits/read",
            id
        });

        JsonDocument response =
            await ReadForIdAsync(id, TimeSpan.FromSeconds(20));

        try
        {
            ThrowIfError(response, "Could not read Codex rate limits");
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private int NextId() =>
        Interlocked.Increment(ref _requestId);

    private async Task SendAsync(object message)
    {
        await _stdin.WriteLineAsync(JsonSerializer.Serialize(message));
        await _stdin.FlushAsync();
    }

    private async Task<JsonDocument> ReadForIdAsync(
        int id,
        TimeSpan timeout)
    {
        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        cts.CancelAfter(timeout);

        while (true)
        {
            string? line;

            try
            {
                line = await _stdout.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Timed out waiting for Codex response {id}.");
            }

            if (line is null)
                throw new InvalidOperationException(
                    "Codex app-server exited unexpectedly.");

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument doc;

            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("id", out var idNode) &&
                idNode.ValueKind == JsonValueKind.Number &&
                idNode.TryGetInt32(out int responseId) &&
                responseId == id)
            {
                return doc;
            }

            doc.Dispose();
        }
    }

    private static void ThrowIfError(
        JsonDocument response,
        string prefix)
    {
        if (!response.RootElement.TryGetProperty(
                "error",
                out JsonElement error))
            return;

        string message =
            error.TryGetProperty("message", out var m)
                ? m.GetString() ?? error.GetRawText()
                : error.GetRawText();

        throw new InvalidOperationException(
            $"{prefix}: {message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lifetime.Cancel();

        try
        {
            _stdin.Close();
        }
        catch
        {
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch
        {
        }

        _process.Dispose();
        _lifetime.Dispose();
    }
}
