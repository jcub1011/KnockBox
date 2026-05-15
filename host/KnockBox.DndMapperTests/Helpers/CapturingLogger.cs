using Microsoft.Extensions.Logging;

namespace KnockBox.DndMapperTests.Helpers
{
    internal sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IEnumerable<(string Message, Exception? Exception)> Warnings =>
            Entries.Where(e => e.Level == LogLevel.Warning).Select(e => (e.Message, e.Exception));

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
