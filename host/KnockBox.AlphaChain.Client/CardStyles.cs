using KnockBox.AlphaChain.Contracts;

namespace KnockBox.AlphaChain.Client;

/// <summary>
/// Pure presentation helpers ported from the server plugin (the server-only
/// <c>CardAccents</c> / <c>ScoreStepColors</c> / per-card palette and the tutorial dwell
/// constants). No server dependencies — safe in the WASM client (mirrors CardCounter's
/// <c>CardDisplay</c>). All colors are CSS <c>var(--…)</c> tokens the theme defines.
/// </summary>
public static class CardStyles
{
    /// <summary>The CSS color token for a card's standardized family accent (border + accent tint).
    /// Ported verbatim from the server <c>CardAccents.Color</c>.</summary>
    public static string AccentColor(CardAccent accent) => accent switch
    {
        CardAccent.Letter => "var(--ac-accent-letter)",
        CardAccent.Clock => "var(--ac-accent-clock)",
        CardAccent.Economy => "var(--ac-accent-economy)",
        CardAccent.Utility => "var(--ac-accent-utility)",
        _ => "var(--ac-accent-neutral)",
    };

    /// <summary>The CSS color a score-replay step's <em>delta</em> renders in: a gain is green, a
    /// loss is red, a zero-change effect ("FX") is violet, and a step that never fired is neutral.
    /// Ported from the server <c>ScoreStepColors.Delta</c>, now taking a <see cref="ScoreStepView"/>.</summary>
    public static string StepDelta(ScoreStepView step)
    {
        if (!step.Triggered)
            return "var(--ac-accent-neutral, #8aa0b3)";          // "—" — never fired
        if (step.ValueText == "FX")
            return "var(--ac-violet, #b97bff)";                  // fired, no score change
        return step.ValueText.StartsWith('+')
            ? "var(--ac-additive, #14f195)"                      // gain
            : "var(--ac-danger, #ff3b5c)";                       // loss
    }

    /// <summary>Cyan for a live, per-player status value — the server <c>CardChips.Live</c> token,
    /// reused for the running-score chip in the submission-history engine strip.</summary>
    public const string LiveChip = "var(--ac-cyan, #00e5ff)";

    /// <summary>Hand-tuned per-card identity color (<c>--gc-card-color</c>), ported verbatim from
    /// the server <c>GameCard.CardColor</c>. Unknown/unmapped falls back to a neutral slate.</summary>
    public static string CardColor(ModifierId id) => id switch
    {
        ModifierId.TheAnchor => "#4f9dff",
        ModifierId.Vanilla => "#f2e2a8",
        ModifierId.ConsonantCrunch => "#ff7a59",
        ModifierId.VocalVowels => "#34c7e6",
        ModifierId.VowelSurge => "#2ed6b6",
        ModifierId.TheArchitect => "#8f8cff",
        ModifierId.BrickLayer => "#d96a3c",
        ModifierId.Speedracer => "#ffd23d",
        ModifierId.LetterHoarder => "#f0a93c",
        ModifierId.Sesquipedalian => "#b06bff",
        ModifierId.GutturalRoar => "#c0603a",
        ModifierId.HighRoller => "#ff5ca0",
        ModifierId.PerfectLink => "#57e08a",
        ModifierId.TheVault => "#9fb3d6",
        ModifierId.Redline => "#ff4d4d",
        ModifierId.PanicButton => "#ff6a2b",
        ModifierId.HyperDrive => "#7d5bff",
        ModifierId.TaxCollector => "#2fa85a",
        ModifierId.IrsAgent => "#b6c24a",
        ModifierId.RouletteWheel => "#ff4fc0",
        ModifierId.TollBooth => "#d98f2e",
        ModifierId.BaitAndSwitch => "#e36ad6",
        ModifierId.Blindfold => "#6f7bff",
        ModifierId.DoubleDown => "#ff5f7a",
        ModifierId.AnchorChain => "#3aa6b8",
        ModifierId.FlakCannon => "#ffb02e",
        ModifierId.BountyHunter => "#cda06a",
        ModifierId.TitaniumMirror => "#b9cfe6",
        ModifierId.HeatSink => "#7fd8ff",
        ModifierId.Prism => "#d3a7ff",
        ModifierId.Wildcard => "#e9e9f2",
        ModifierId.Catalyst => "#b6e84a",
        _ => "#8aa0b3",
    };

    /// <summary>The tutorial dwell durations the server FSM auto-advances on (single source of
    /// truth there: <c>TutorialState</c>). Ported as client constants so the progress bar fills
    /// over the same window. Seconds.</summary>
    public static double TutorialDwellSeconds(TutorialKind kind) => kind switch
    {
        TutorialKind.Shiritori => 12,
        TutorialKind.Engine => 14,
        TutorialKind.Tax => 12,
        _ => 12,
    };
}
