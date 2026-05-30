using Microsoft.Extensions.Logging;

namespace KnockBox.Tracery.Tests.Helpers
{
    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> that records every entry so tests can assert on what
    /// was logged — used to prove side effects that have no other observable surface, e.g. that
    /// the engine builds its dictionary trie exactly once across many lobbies/rounds.
    /// </summary>
    internal sealed class ListLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        /// <summary>How many recorded messages contain <paramref name="fragment"/> (ordinal, case-insensitive).</summary>
        public int CountContaining(string fragment)
            => Entries.Count(e => e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
