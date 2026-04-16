using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.State.Games.Data;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class GuessForm : ComponentBase
    {
        [Parameter, EditorRequired] public IReadOnlyList<SecretTask> TaskPool { get; set; } = default!;
        [Parameter, EditorRequired] public IReadOnlyList<HiddenAgendaPlayerState> Opponents { get; set; } = default!;
        [Parameter] public Action<Dictionary<string, List<string>>>? OnSubmit { get; set; }

        protected Dictionary<string, List<string>> Guesses { get; set; } = new();

        protected override void OnInitialized()
        {
            foreach (var opponent in Opponents)
            {
                Guesses[opponent.PlayerId] = new List<string> { "", "", "" };
            }
        }

        private bool Validate()
        {
            foreach (var opponent in Opponents)
            {
                var selections = Guesses[opponent.PlayerId].Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (selections.Count != 3) return false;
                if (selections.Distinct().Count() != 3) return false;
            }
            return true;
        }

        private void Submit()
        {
            if (Validate())
            {
                OnSubmit?.Invoke(Guesses);
            }
        }
    }
}
