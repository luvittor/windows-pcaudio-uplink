namespace WindowsPcAudioUplink;

public static class ReconnectPolicy
{
    public static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(5);

    public static TimeSpan GetDelay(int attempt, TimeSpan disconnectedFor)
    {
        if (disconnectedFor >= DegradedAfter)
        {
            return TimeSpan.FromMinutes(1);
        }

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(Math.Max(attempt - 1, 0), 5))));
    }
}
