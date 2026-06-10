namespace KnockBox.Operator.Client;

/// <summary>
/// Pure client-side time formatting for activity-log / history stamps. Renders a server
/// <see cref="System.DateTimeOffset"/> as a relative "Ns/Nm/Nh ago" label against the
/// browser clock (which tracks the server's closely enough for a coarse stamp).
/// </summary>
internal static class OperatorTime
{
    public static string Relative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        return $"{(int)delta.TotalHours}h ago";
    }
}
