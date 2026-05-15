using KnockBox.Core.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class DndMapperToast : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperToastService Service { get; set; } = default!;

        private readonly Dictionary<Guid, CancellationTokenSource> _timers = [];

        protected override void OnInitialized()
        {
            Service.Changed += OnServiceChanged;
            base.OnInitialized();
        }

        private async Task OnServiceChanged()
        {
            // Schedule auto-dismiss for any new entries.
            foreach (var entry in Service.Entries)
            {
                if (_timers.ContainsKey(entry.Id)) continue;
                var cts = new CancellationTokenSource();
                _timers[entry.Id] = cts;
                _ = AutoDismissAsync(entry.Id, cts.Token);
            }

            // Cancel timers for entries that no longer exist (manual dismiss).
            var liveIds = Service.Entries.Select(e => e.Id).ToHashSet();
            foreach (var id in _timers.Keys.Where(k => !liveIds.Contains(k)).ToList())
            {
                _timers[id].Cancel();
                _timers[id].Dispose();
                _timers.Remove(id);
            }

            await InvokeAsync(StateHasChanged);
        }

        private async Task AutoDismissAsync(Guid id, CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
                await Service.Dismiss(id);
            }
            catch (TaskCanceledException) { /* superseded */ }
        }

        private static string CssFor(DndMapperToastEntry entry) => entry.Tone switch
        {
            DndMapperToastTone.Success => "dndm-toast dndm-toast--success",
            DndMapperToastTone.Warning => "dndm-toast dndm-toast--warn",
            DndMapperToastTone.Danger => "dndm-toast dndm-toast--danger",
            _ => "dndm-toast",
        };

        private static string IconFor(DndMapperToastTone tone) => tone switch
        {
            DndMapperToastTone.Success => "✓",
            DndMapperToastTone.Warning => "!",
            DndMapperToastTone.Danger => "✕",
            _ => "•",
        };

        public override void Dispose()
        {
            Service.Changed -= OnServiceChanged;
            foreach (var cts in _timers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _timers.Clear();
            base.Dispose();
        }
    }
}
