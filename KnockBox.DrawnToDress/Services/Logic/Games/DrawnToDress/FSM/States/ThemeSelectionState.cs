using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Announces or selects the theme for this drawing session.
    ///
    /// Transition ownership:
    /// - When <c>ThemeSource.Random</c>: immediately chains to <see cref="DrawingRoundState"/>
    ///   on entry (no player input required).
    /// - <see cref="SelectThemeCommand"/> (host only, <c>ThemeSource.HostPick</c>)
    ///   → <see cref="DrawingRoundState"/>
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// - <see cref="AbandonGameCommand"/> (host only) → <see cref="AbandonedState"/>
    /// </summary>
    public sealed class ThemeSelectionState : IDrawnToDressGameState
    {
        /// <summary>
        /// Placeholder theme list used when no themes have been configured.
        /// Later issues will populate this from a proper theme repository.
        /// </summary>
        private static readonly ThemeDefinition[] FallbackThemes =
        [
            new("retro_futurism", "Retro Futurism",
                "Outfits inspired by 1950s-style visions of the future."),
            new("enchanted_forest", "Enchanted Forest",
                "Mystical nature-themed outfits from a world of fairy tales."),
            new("street_style", "Street Style",
                "Urban casual wear with bold attitude."),
        ];

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.ThemeSelection);
            context.Logger.LogInformation("FSM → ThemeSelectionState");

            // For random theme source, pick a theme immediately and chain to drawing.
            if (context.Config.ThemeSource == ThemeSource.Random)
            {
                SelectRandomTheme(context);
                context.Logger.LogInformation(
                    "Random theme selected: [{id}] \"{name}\".",
                    context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
                return new DrawingRoundState();
            }

            // For host-pick, remain in this state and wait for a SelectThemeCommand.
            return null;
        }

        public Result OnExit(DrawnToDressGameContext context) => Result.Success;

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case SelectThemeCommand cmd:
                    if (cmd.PlayerId != context.State.Host.Id)
                    {
                        context.Logger.LogWarning(
                            "SelectTheme rejected: player [{id}] is not the host.", cmd.PlayerId);
                        return null;
                    }
                    context.State.CurrentTheme = new ThemeDefinition(cmd.ThemeId, cmd.ThemeId);
                    context.Logger.LogInformation(
                        "Host selected theme [{id}].", cmd.ThemeId);
                    return new DrawingRoundState();

                case PauseGameCommand:
                    return new PausedState(this);

                case AbandonGameCommand:
                    return new AbandonedState();

                default:
                    return null;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SelectRandomTheme(DrawnToDressGameContext context)
        {
            // TODO: Replace with a proper injected theme repository in a later issue.
            var themes = FallbackThemes;
            var theme = themes[Random.Shared.Next(themes.Length)];
            context.State.CurrentTheme = theme;
        }
    }
}
