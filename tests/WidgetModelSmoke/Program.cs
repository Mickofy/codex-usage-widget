using CodexUsageWidget;

static void Expect(double actual, double expected, string label)
{
    if (Math.Abs(actual - expected) > 0.0001)
        throw new Exception($"{label}: expected {expected}, got {actual}");
}

Expect(new UsageWindow(300, 0, null).RemainingPercent, 100, "No usage");
Expect(new UsageWindow(300, 25.5, null).RemainingPercent, 74.5, "Partial usage");
Expect(new UsageWindow(300, 100, null).RemainingPercent, 0, "At limit");
Expect(new UsageWindow(300, 140, null).RemainingPercent, 0, "Out-of-range percentage");
Expect(new UsageWindow(300, -3, null).RemainingPercent, 100, "Negative usage");

var snapshot = new UsageSnapshot(new UsageWindow(300, 22, null), null, DateTimeOffset.UtcNow);
if (snapshot.FiveHour?.WindowDurationMinutes != 300 || snapshot.Weekly is not null)
    throw new Exception("UsageSnapshot values were not retained");

Console.WriteLine("Widget model smoke tests passed (offline; no Codex credentials used).");
