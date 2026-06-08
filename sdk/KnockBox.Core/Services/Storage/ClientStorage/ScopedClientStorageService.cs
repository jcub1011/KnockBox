using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.ClientStorage
{
    /// <summary>
    /// Wraps an inner <see cref="IClientStorageService"/> and transparently
    /// namespaces every operation under a plugin's route identifier. A plugin
    /// that stores through this wrapper can't accidentally read, overwrite, or
    /// clear another plugin's (or the host's) client-storage data, and need not
    /// worry about scope-name collisions with other plugins.
    /// <para>
    /// The wrapper rewrites the caller's <c>scope</c> to <c>"{route}::{scope}"</c>
    /// before delegating, so the physical browser key becomes
    /// <c>"{route}::{scope}.{key}"</c>. <see cref="GetAllKeysAsync"/> only
    /// surfaces keys inside the plugin's namespace (with the prefix stripped),
    /// and <see cref="ClearAsync"/> only clears the plugin's namespace — it
    /// never wipes the whole browser store the way the raw service does.
    /// </para>
    /// <para>
    /// This is collision-avoidance, not a sandbox: a plugin can still reach the
    /// raw browser storage directly. The aim is to make cross-plugin clobbering
    /// hard to do <i>by accident</i>.
    /// </para>
    /// </summary>
    public sealed class ScopedClientStorageService : IClientStorageService
    {
        // Route identifiers match ^[a-z0-9-]+$ and never contain ':', so this
        // separator can't collide with a host or plugin scope literal.
        private const string Separator = "::";

        private readonly IClientStorageService _inner;
        private readonly string _scopePrefix; // "{route}::"

        public ScopedClientStorageService(IClientStorageService inner, string routeIdentifier)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentException.ThrowIfNullOrWhiteSpace(routeIdentifier);
            _inner = inner;
            _scopePrefix = routeIdentifier + Separator;
        }

        public ValueTask<ValueResult<TType?>> GetAsync<TType>(string scope, string key, CancellationToken ct = default)
            => _inner.GetAsync<TType>(Prefix(scope), key, ct);

        public ValueTask<Result> SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default)
            => _inner.SetAsync(Prefix(scope), key, value, ct);

        public ValueTask<Result> RemoveAsync(string scope, string key)
            => _inner.RemoveAsync(Prefix(scope), key);

        public ValueTask<Result> RemoveAsync(string scope)
            => _inner.RemoveAsync(Prefix(scope));

        public async ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default)
        {
            var result = await _inner.GetKeysAsync(Prefix(scope), ct).ConfigureAwait(false);
            return StripPrefix(result);
        }

        public async ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default)
        {
            // The raw service returns every key in the browser store, including
            // the host's and other plugins'. Filter to this plugin's namespace
            // and hand back the keys with the route prefix removed.
            var result = await _inner.GetAllKeysAsync(ct).ConfigureAwait(false);
            return StripPrefix(result);
        }

        public async ValueTask<Result> ClearAsync()
        {
            // Never delegate to inner.ClearAsync() — that wipes the entire
            // browser store, including the host's session token and every other
            // plugin's data. Clear only this plugin's namespace.
            var keysResult = await _inner.GetAllKeysAsync().ConfigureAwait(false);
            if (keysResult.IsCanceled) return Result.FromCancellation();
            if (keysResult.TryGetFailure(out var failure)) return Result.FromError(failure);
            if (!keysResult.TryGetSuccess(out var keys) || keys.Count == 0) return Result.Success;

            // Each stored key is the physical "{route}::{scope}.{key}". Remove the
            // namespace's keys one at a time, splitting at the LAST '.' into the
            // (scope, key) pair the inner service rejoins as "{scope}.{key}". That
            // pair round-trips back to exactly this physical key even when the
            // scope or key itself contains a '.' — a first-'.' split would
            // mis-derive the scope and silently leave the key behind.
            foreach (var k in keys)
            {
                if (!k.StartsWith(_scopePrefix, StringComparison.Ordinal)) continue;
                var dot = k.LastIndexOf('.');
                if (dot <= 0 || dot == k.Length - 1) continue;

                var removeResult = await _inner.RemoveAsync(k[..dot], k[(dot + 1)..]).ConfigureAwait(false);
                if (removeResult.IsCanceled) return Result.FromCancellation();
                if (removeResult.TryGetFailure(out var removeError)) return Result.FromError(removeError);
            }
            return Result.Success;
        }

        public ValueTask DisposeAsync()
            // The inner service is owned by DI (circuit-scoped); the wrapper
            // doesn't own its lifetime, so there's nothing to dispose here.
            => ValueTask.CompletedTask;

        private string Prefix(string scope)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            return _scopePrefix + scope;
        }

        private ValueResult<List<string>> StripPrefix(ValueResult<List<string>> result)
        {
            if (result.IsCanceled) return ValueResult<List<string>>.FromCancellation();
            if (result.TryGetFailure(out var failure)) return ValueResult<List<string>>.FromError(failure);
            result.TryGetSuccess(out var keys);
            var stripped = (keys ?? [])
                .Where(k => k.StartsWith(_scopePrefix, StringComparison.Ordinal))
                .Select(k => k[_scopePrefix.Length..])
                .ToList();
            return ValueResult<List<string>>.FromValue(stripped);
        }
    }
}
