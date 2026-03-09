namespace KnockBox.Extensions.Disposable
{
    /// <summary>
    /// Wraps an <see cref="IDisposable"/> object and automatically disposes it when the wrapper is
    /// disposed or garbage collected. Safe to call dispose multiple times; the inner object will
    /// only ever be disposed once.
    /// </summary>
    public sealed class DisposingWrapper : IDisposable
    {
        private IDisposable? _inner;

        public DisposingWrapper(IDisposable inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        ~DisposingWrapper()
        {
            var inner = Interlocked.Exchange(ref _inner, null);
            inner?.Dispose();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            var inner = Interlocked.Exchange(ref _inner, null);
            inner?.Dispose();
        }
    }
}
