using KnockBox.HiddenAgenda.Services.State.Games;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.AspNetCore.Components;

namespace KnockBox.HiddenAgenda.Components
{
    public partial class DossierPanel : ComponentBase
    {
        [Parameter, EditorRequired] public IReadOnlyList<SecretTask> TaskPool { get; set; } = default!;
        [Parameter] public List<SecretTask>? PlayerTasks { get; set; }

        private bool _isCollapsed = true;

        private void Toggle() => _isCollapsed = !_isCollapsed;

        private static readonly string[] Categories = ["Devotion", "Style", "Movement", "Neglect", "Rivalry"];

        private IEnumerable<SecretTask> GetTasksByCategory(string category)
        {
            return TaskPool.Where(t => t.Category.ToString() == category).OrderBy(t => t.Id);
        }

        private bool IsPlayerTask(string taskId)
        {
            return PlayerTasks?.Any(t => t.Id == taskId) ?? false;
        }
    }
}
