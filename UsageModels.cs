namespace CodexUsageWidget;

internal sealed record UsageWindow(
    int WindowDurationMinutes,
    double UsedPercent,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent =>
        Math.Clamp(100d - UsedPercent, 0d, 100d);
}

internal sealed record UsageSnapshot(
    UsageWindow? FiveHour,
    UsageWindow? Weekly,
    DateTimeOffset RetrievedAt);

internal enum UsageProvider
{
    Codex,
    Gemini
}

internal sealed record GeminiUsageSnapshot(
    double RemainingPercent,
    int? RemainingRequests,
    int? RequestLimit,
    DateTimeOffset? ResetsAt,
    DateTimeOffset RetrievedAt);
