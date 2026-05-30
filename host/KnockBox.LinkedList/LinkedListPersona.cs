namespace KnockBox.LinkedList;

/// <summary>
/// The Auditor's cosmetic persona (§6). Purely flavor — it has <b>no</b> effect on
/// <c>Approve</c>/<c>Reject</c> outcomes; it's shown on the Auditor and spectator
/// views as banter plus an informal difficulty hint for the table.
/// </summary>
public enum AuditorPersona { Neutral, MercilessJudge, EasyMark, Pedant, WildCard }

/// <summary>Display metadata for an <see cref="AuditorPersona"/>.</summary>
public sealed record PersonaInfo(AuditorPersona Persona, string Label, string Emoji, string Hint);

/// <summary>Lookup of persona display metadata, ordered for the persona dial.</summary>
public static class AuditorPersonaInfo
{
    public static readonly IReadOnlyList<PersonaInfo> All =
    [
        new(AuditorPersona.Neutral,        "Neutral",         "⚖️", "Plays it straight."),
        new(AuditorPersona.MercilessJudge, "Merciless Judge", "🔨", "Expect to fight for every pair."),
        new(AuditorPersona.EasyMark,       "Easy Mark",       "😌", "Generous — almost anything goes."),
        new(AuditorPersona.Pedant,         "Pedant",          "🤓", "Sweats the details."),
        new(AuditorPersona.WildCard,       "Wild Card",       "🎲", "Anyone's guess."),
    ];

    public static PersonaInfo Of(AuditorPersona persona) =>
        All.FirstOrDefault(p => p.Persona == persona) ?? All[0];
}
