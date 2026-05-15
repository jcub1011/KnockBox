namespace KnockBox.DndMapper.Pages.Components
{
    public enum DndMapperToastTone { Info, Success, Warning, Danger }

    public sealed record DndMapperToastEntry(Guid Id, string Message, DndMapperToastTone Tone, DateTime CreatedUtc);

    /// <summary>
    /// Lightweight per-page toast queue. Owned by <see cref="DndMapperPlayingPhase"/>
    /// and cascaded to descendants. Components call <see cref="Push"/>; the
    /// <see cref="DndMapperToast"/> component renders the queue and auto-dismisses
    /// each entry after ~4 seconds.
    /// </summary>
    public sealed class DndMapperToastService
    {
        private readonly List<DndMapperToastEntry> _entries = [];

        public IReadOnlyList<DndMapperToastEntry> Entries => _entries;

        public event Func<Task>? Changed;

        public async Task Push(string message, DndMapperToastTone tone = DndMapperToastTone.Info)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var entry = new DndMapperToastEntry(Guid.NewGuid(), message, tone, DateTime.UtcNow);
            _entries.Add(entry);
            if (_entries.Count > 5)
            {
                _entries.RemoveRange(0, _entries.Count - 5);
            }
            await NotifyChangedAsync();
        }

        public async Task Dismiss(Guid id)
        {
            if (_entries.RemoveAll(e => e.Id == id) > 0)
            {
                await NotifyChangedAsync();
            }
        }

        private async Task NotifyChangedAsync()
        {
            if (Changed is null) return;
            foreach (var handler in Changed.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
    }
}
