using System.Diagnostics;
using System.Text.Json;

namespace CodexUsageWidget;

internal sealed class GeminiUsageService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    private GeminiUsageSnapshot? _cached;
    private DateTimeOffset _cachedAt;

    public async Task<GeminiUsageSnapshot> GetUsageAsync(bool force = false)
    {
        if (!force &&
            _cached is not null &&
            DateTimeOffset.Now - _cachedAt < CacheLifetime)
        {
            return _cached;
        }

        string executable = FindAgyExecutable()
            ?? throw new InvalidOperationException(
                "Antigravity CLI is not installed. Install it, then confirm `agy --version` works.");

        ProcessResult result = await RunUsageAsync(executable);

        if (result.ExitCode != 0)
        {
            string combined = $"{result.StandardOutput}\n{result.StandardError}";

            if (IsSignedOut(combined))
            {
                throw new InvalidOperationException(
                    "Antigravity CLI needs sign-in. Run `agy` once, sign in with Google, then refresh the widget.");
            }

            string error = FirstUsefulLine(result.StandardError)
                ?? FirstUsefulLine(result.StandardOutput)
                ?? $"Antigravity CLI exited with code {result.ExitCode}.";

            throw new InvalidOperationException(error);
        }

        GeminiUsageSnapshot snapshot = ParseSnapshot(result.StandardOutput);
        _cached = snapshot;
        _cachedAt = DateTimeOffset.Now;
        return snapshot;
    }

    private static async Task<ProcessResult> RunUsageAsync(string executable)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("/usage");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--log-file");
        psi.ArgumentList.Add(Path.GetTempFileName());

        // Antigravity checks for updates on normal startup. A passive meter
        // should only read quota and should not update the CLI in the background.
        psi.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start Antigravity CLI.");
        }
        catch (Exception ex) when (
            ex is System.ComponentModel.Win32Exception ||
            ex is FileNotFoundException)
        {
            throw new InvalidOperationException(
                "Antigravity CLI is not available. Confirm `agy --version` works.",
                ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(CommandTimeout);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException(
                "Antigravity usage check timed out after 20 seconds.");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static GeminiUsageSnapshot ParseSnapshot(string rawOutput)
    {
        int jsonStart = rawOutput.IndexOf('{');
        if (jsonStart < 0)
        {
            throw new InvalidOperationException(
                "Antigravity usage returned an unexpected response.");
        }

        string json = rawOutput[jsonStart..];

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("status", out JsonElement status) &&
                !string.Equals(
                    status.GetString(),
                    "SUCCESS",
                    StringComparison.OrdinalIgnoreCase))
            {
                string? error = root.TryGetProperty("error", out JsonElement errorElement)
                    ? errorElement.GetString()
                    : null;

                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Antigravity usage request failed."
                        : error);
            }

            if (!TryGetGeminiBucket(root, out JsonElement bucket))
            {
                throw new InvalidOperationException(
                    "Gemini quota was not found in Antigravity /usage.");
            }

            double remainingFraction = bucket.GetProperty("remaining_fraction").GetDouble();
            double remainingPercent = Math.Clamp(remainingFraction * 100d, 0d, 100d);

            DateTimeOffset? resetsAt = null;
            if (remainingFraction < 1d &&
                bucket.TryGetProperty("reset_time", out JsonElement resetElement) &&
                resetElement.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(resetElement.GetString(), out DateTimeOffset parsedReset))
            {
                resetsAt = parsedReset;
            }

            return new GeminiUsageSnapshot(
                remainingPercent,
                RemainingRequests: null,
                RequestLimit: null,
                resetsAt,
                DateTimeOffset.Now);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Antigravity usage returned invalid JSON.",
                ex);
        }
    }

    private static bool TryGetGeminiBucket(
        JsonElement root,
        out JsonElement selectedBucket)
    {
        selectedBucket = default;

        if (!root.TryGetProperty("command", out JsonElement command) ||
            !command.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("groups", out JsonElement groups) ||
            groups.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        JsonElement? best = null;
        double bestRemaining = double.MaxValue;
        bool bestIsWeekly = false;

        foreach (JsonElement group in groups.EnumerateArray())
        {
            string? name = group.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name) ||
                !name.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
                !group.TryGetProperty("buckets", out JsonElement buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement bucket in buckets.EnumerateArray())
            {
                if (bucket.TryGetProperty("disabled", out JsonElement disabled) &&
                    disabled.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (!bucket.TryGetProperty("remaining_fraction", out JsonElement fraction) ||
                    fraction.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                double remaining = fraction.GetDouble();
                bool isWeekly =
                    bucket.TryGetProperty("window", out JsonElement window) &&
                    string.Equals(
                        window.GetString(),
                        "weekly",
                        StringComparison.OrdinalIgnoreCase);

                if (best is null ||
                    (isWeekly && !bestIsWeekly) ||
                    (isWeekly == bestIsWeekly && remaining < bestRemaining))
                {
                    best = bucket.Clone();
                    bestRemaining = remaining;
                    bestIsWeekly = isWeekly;
                }
            }
        }

        if (best is null)
            return false;

        selectedBucket = best.Value;
        return true;
    }

    private static string? FindAgyExecutable()
    {
        string localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        string installed =
            Path.Combine(localAppData, "agy", "bin", "agy.exe");

        if (File.Exists(installed))
            return installed;

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string entry in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(entry, "agy.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static bool IsSignedOut(string text)
    {
        return text.Contains(
                   "authentication required",
                   StringComparison.OrdinalIgnoreCase) ||
               text.Contains(
                   "stored credentials are expired or revoked",
                   StringComparison.OrdinalIgnoreCase) ||
               text.Contains(
                   "not logged into Antigravity",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstUsefulLine(string text)
    {
        return text
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
