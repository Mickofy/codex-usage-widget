using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexUsageWidget;

internal sealed class GeminiUsageService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

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

        await EnsureGeminiAvailableAsync();

        string output = await GeminiStatsProbe.ReadUsageAsync();
        GeminiUsageSnapshot snapshot = ParseSnapshot(output);

        _cached = snapshot;
        _cachedAt = DateTimeOffset.Now;
        return snapshot;
    }

    private static async Task EnsureGeminiAvailableAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /s /c \"gemini --version\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start Gemini CLI.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Gemini CLI is not installed. Install it, then confirm `gemini --version` works.",
                ex);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

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

            throw new TimeoutException("Timed out while checking Gemini CLI.");
        }

        if (process.ExitCode != 0)
        {
            string error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Gemini CLI is not available. Confirm `gemini --version` works."
                    : error.Trim());
        }
    }

    private static GeminiUsageSnapshot ParseSnapshot(string rawOutput)
    {
        string output = StripTerminalSequences(rawOutput);

        Match usedMatch = Regex.Match(
            output,
            @"(?<used>\d+(?:\.\d+)?)\s*%\s*used(?:\s*\(Limit\s+resets\s+in\s+(?<reset>[^)\r\n]+)\))?",
            RegexOptions.IgnoreCase);

        double remainingPercent;
        string? resetText = null;

        if (usedMatch.Success &&
            double.TryParse(
                usedMatch.Groups["used"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double usedPercent))
        {
            remainingPercent = Math.Clamp(100d - usedPercent, 0d, 100d);
            resetText = usedMatch.Groups["reset"].Success
                ? usedMatch.Groups["reset"].Value.Trim()
                : null;
        }
        else
        {
            Match reachedMatch = Regex.Match(
                output,
                @"Limit\s+reached(?:,?\s*resets\s+in\s+(?<reset>[^\r\n]+))?",
                RegexOptions.IgnoreCase);

            if (reachedMatch.Success)
            {
                remainingPercent = 0d;
                resetText = reachedMatch.Groups["reset"].Success
                    ? reachedMatch.Groups["reset"].Value.Trim()
                    : null;
            }
            else
            {
                // Compatibility with older Gemini CLI quota tables.
                Match legacyMatch = Regex.Match(
                    output,
                    @"gemini-[^\s│]+\s+(?:-|\d+)\s+(?<remaining>\d+(?:\.\d+)?)%\s*\(Resets\s+in\s+(?<reset>[^)]+)\)",
                    RegexOptions.IgnoreCase);

                if (!legacyMatch.Success ||
                    !double.TryParse(
                        legacyMatch.Groups["remaining"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out remainingPercent))
                {
                    throw BuildParseException(output);
                }

                remainingPercent = Math.Clamp(remainingPercent, 0d, 100d);
                resetText = legacyMatch.Groups["reset"].Value.Trim();
            }
        }

        DateTimeOffset? modelReset = FindLatestModelReset(output);

        int? limit = null;
        Match limitMatch = Regex.Match(
            output,
            @"Usage\s+limit:\s*(?<limit>[\d,]+)",
            RegexOptions.IgnoreCase);

        if (limitMatch.Success &&
            int.TryParse(
                limitMatch.Groups["limit"].Value.Replace(",", string.Empty),
                out int parsedLimit))
        {
            limit = parsedLimit;
        }

        int? remainingRequests = limit is int requestLimit
            ? (int)Math.Round(requestLimit * remainingPercent / 100d)
            : null;

        DateTimeOffset? resetsAt =
            ParseRelativeReset(resetText) ?? modelReset;

        return new GeminiUsageSnapshot(
            remainingPercent,
            remainingRequests,
            limit,
            resetsAt,
            DateTimeOffset.Now);
    }

    private static InvalidOperationException BuildParseException(string output)
    {
        if (output.Contains("Sign in", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("login", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("authentication", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException(
                "Gemini CLI needs sign-in. Open `gemini` once in a terminal, sign in, then refresh the widget.");
        }

        return new InvalidOperationException(
            "Gemini quota was not found. Open `gemini` once and confirm the quota indicator is visible, then refresh the widget.");
    }

    private static DateTimeOffset? FindLatestModelReset(string output)
    {
        DateTimeOffset? latest = null;

        foreach (Match match in Regex.Matches(
                     output,
                     @"Resets:\s*[^\r\n]*?\((?<relative>[^)]+)\)",
                     RegexOptions.IgnoreCase))
        {
            DateTimeOffset? candidate =
                ParseRelativeReset(match.Groups["relative"].Value);

            if (candidate is not null &&
                (latest is null || candidate > latest))
            {
                latest = candidate;
            }
        }

        return latest;
    }

    private static DateTimeOffset? ParseRelativeReset(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = Regex.Match(
            text,
            @"(?:(?<days>\d+)d)?\s*(?:(?<hours>\d+)h)?\s*(?:(?<minutes>\d+)m)?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            return null;

        int days = ParsePart(match, "days");
        int hours = ParsePart(match, "hours");
        int minutes = ParsePart(match, "minutes");

        if (days == 0 && hours == 0 && minutes == 0)
            return null;

        return DateTimeOffset.Now.AddDays(days).AddHours(hours).AddMinutes(minutes);
    }

    private static int ParsePart(Match match, string group)
    {
        return match.Groups[group].Success &&
               int.TryParse(match.Groups[group].Value, out int value)
            ? value
            : 0;
    }

    private static string StripTerminalSequences(string text)
    {
        text = Regex.Replace(
            text,
            @"\x1B\][^\x07]*(?:\x07|\x1B\\)",
            string.Empty);

        text = Regex.Replace(
            text,
            @"\x1B\[[0-?]*[ -/]*[@-~]",
            string.Empty);

        return text.Replace("\0", string.Empty);
    }
}

internal static class GeminiStatsProbe
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint HandleFlagInherit = 0x00000001;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;

    public static async Task<string> ReadUsageAsync()
    {
        IntPtr inputRead = IntPtr.Zero;
        IntPtr inputWrite = IntPtr.Zero;
        IntPtr outputRead = IntPtr.Zero;
        IntPtr outputWrite = IntPtr.Zero;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        Process? process = null;

        try
        {
            CreatePipeChecked(out inputRead, out inputWrite);
            CreatePipeChecked(out outputRead, out outputWrite);

            SetHandleInformation(inputWrite, HandleFlagInherit, 0);
            SetHandleInformation(outputRead, HandleFlagInherit, 0);

            int hr = CreatePseudoConsole(
                new Coord(180, 60),
                inputRead,
                outputWrite,
                0,
                out pseudoConsole);

            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            CloseHandle(inputRead);
            inputRead = IntPtr.Zero;
            CloseHandle(outputWrite);
            outputWrite = IntPtr.Zero;

            nuint attributeSize = 0;
            _ = InitializeProcThreadAttributeList(
                IntPtr.Zero,
                1,
                0,
                ref attributeSize);

            attributeList = Marshal.AllocHGlobal((IntPtr)attributeSize);

            if (!InitializeProcThreadAttributeList(
                    attributeList,
                    1,
                    0,
                    ref attributeSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (nuint)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var startupInfo = new StartupInfoEx();
            startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startupInfo.lpAttributeList = attributeList;

            string comSpec =
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            var commandLine = new StringBuilder(
                $"\"{comSpec}\" /d /s /c gemini");

            string workingDirectory =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

            if (!CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out ProcessInformation processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            CloseHandle(processInfo.hThread);

            try
            {
                process = Process.GetProcessById(processInfo.dwProcessId);
            }
            finally
            {
                CloseHandle(processInfo.hProcess);
            }

            using var inputHandle =
                new SafeFileHandle(inputWrite, ownsHandle: true);
            inputWrite = IntPtr.Zero;

            using var outputHandle =
                new SafeFileHandle(outputRead, ownsHandle: true);
            outputRead = IntPtr.Zero;

            await using var inputStream =
                new FileStream(inputHandle, FileAccess.Write, 4096, true);
            await using var outputStream =
                new FileStream(outputHandle, FileAccess.Read, 4096, true);

            using var writer = new StreamWriter(
                inputStream,
                new UTF8Encoding(false),
                1024,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n"
            };

            using var reader = new StreamReader(
                outputStream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);

            var output = new StringBuilder();
            var readCts = new CancellationTokenSource();
            Task readTask = ReadOutputAsync(reader, output, readCts.Token);

            await Task.Delay(2200);
            await writer.WriteLineAsync("/stats");

            // /stats refreshes quota state. /model then renders Gemini CLI's
            // own Model usage rows, which include reset times without sending
            // a model prompt or consuming a request.
            await Task.Delay(2800);
            await writer.WriteLineAsync("/model");

            DateTimeOffset deadline =
                DateTimeOffset.Now.AddSeconds(14);

            while (DateTimeOffset.Now < deadline)
            {
                string snapshot;
                lock (output)
                    snapshot = output.ToString();

                if (HasQuotaOutput(snapshot))
                    break;

                if (process.HasExited)
                    break;

                await Task.Delay(250);
            }

            try
            {
                await writer.WriteLineAsync("/quit");
                await Task.Delay(350);
            }
            catch
            {
            }

            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
            }

            readCts.Cancel();

            try
            {
                await readTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
            }

            lock (output)
                return output.ToString();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Gemini usage switching requires Windows 10 1809 or later.",
                ex);
        }
        finally
        {
            if (process is not null)
                process.Dispose();

            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (pseudoConsole != IntPtr.Zero)
                ClosePseudoConsole(pseudoConsole);

            CloseIfNeeded(inputRead);
            CloseIfNeeded(inputWrite);
            CloseIfNeeded(outputRead);
            CloseIfNeeded(outputWrite);
        }
    }

    private static async Task ReadOutputAsync(
        StreamReader reader,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        var buffer = new char[2048];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken);

                if (read == 0)
                    break;

                lock (output)
                    output.Append(buffer, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static bool HasQuotaOutput(string output)
    {
        bool hasUsage =
            output.Contains("% used", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Limit reached", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Usage left", StringComparison.OrdinalIgnoreCase);

        bool hasReset =
            output.Contains("Model usage", StringComparison.OrdinalIgnoreCase) &&
            output.Contains("Resets:", StringComparison.OrdinalIgnoreCase);

        // Some auth modes do not provide reset timestamps. In that case the
        // loop will naturally run until its short deadline and we still keep
        // the real percentage instead of inventing a reset time.
        return hasUsage && hasReset;
    }

    private static void CreatePipeChecked(
        out IntPtr read,
        out IntPtr write)
    {
        if (!CreatePipe(out read, out write, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void CloseIfNeeded(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;

        public Coord(short x, short y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        IntPtr lpPipeAttributes,
        uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        IntPtr hObject,
        uint dwMask,
        uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(
        Coord size,
        IntPtr hInput,
        IntPtr hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        nuint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(
        IntPtr lpAttributeList);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
