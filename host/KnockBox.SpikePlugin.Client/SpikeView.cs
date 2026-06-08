namespace KnockBox.SpikePlugin.Client;

/// <summary>
/// The plugin's OWN deserialization shape for the projected view JSON. It has no
/// <c>JsonSerializerContext</c> on purpose: deserializing into it exercises the
/// reflection-based JSON path the trimmed client must keep working for unknown
/// third-party DTOs (Phase 0 kill-criterion #1).
/// </summary>
public sealed class SpikeView
{
    public string Phase { get; set; } = "";
    public int CurrentRound { get; set; }
    public bool IsJoinable { get; set; }
    public Guid? CurrentPlayerId { get; set; }
    public List<SpikeRosterEntry> Roster { get; set; } = [];
    public List<SpikePlayerView> Players { get; set; } = [];
}

public sealed class SpikeRosterEntry
{
    public Guid PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
}

public sealed class SpikePlayerView
{
    public Guid PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
    public int CumulativeScore { get; set; }
    public int CurrentSpaceId { get; set; }
    public bool HasSubmittedGuess { get; set; }

    /// <summary>Present only for the recipient's own entry (server-side default-deny).</summary>
    public List<SpikeSecretTask>? SecretTasks { get; set; }
}

public sealed class SpikeSecretTask
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
}
