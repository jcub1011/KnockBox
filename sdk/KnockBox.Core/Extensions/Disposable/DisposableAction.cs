namespace KnockBox.Core.Extensions.Disposable
{
    /// <summary>
    /// Invokes the provided action when disposed. Safe to invoke dispose multiple times as the action will only ever be called once.
    /// </summary>
    /// <param name="disposeAction"></param>
    public sealed class DisposableAction(Action disposeAction) : IDisposable
    {
        private Action? _disposeAction = disposeAction
            ?? throw new ArgumentNullException(nameof(disposeAction));

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _disposeAction, null);
            action?.Invoke();
        }
    }
}
