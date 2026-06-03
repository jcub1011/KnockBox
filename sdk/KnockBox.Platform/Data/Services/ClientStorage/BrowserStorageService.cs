using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.JSInterop;
using System.Text.Json;

namespace KnockBox.Data.Services.ClientStorage
{
    internal abstract class BrowserStorageService : IClientStorageService
    {
        private readonly string _storageName;
        private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
        private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

        protected BrowserStorageService(IJSRuntime jsRuntime, string storageName)
        {
            ArgumentNullException.ThrowIfNull(jsRuntime);
            _storageName = storageName;

            _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
                jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/KnockBox.Platform/js/localStorageService.js").AsTask());
        }

        public async ValueTask<Result> ClearAsync()
        {
            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("clear", CancellationToken.None, _storageName).ConfigureAwait(false);
                return Result.Success;
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (Exception ex)
            {
                return Result.FromError("Unable to clear client storage.", $"Error clearing client storage: {ex}");
            }
        }

        public async ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default)
        {
            try
            {
                var module = await _moduleTask.Value.ConfigureAwait(false);
                var keys = await module.InvokeAsync<string[]>("getAllKeys", ct, _storageName).ConfigureAwait(false);
                return keys?.ToList() ?? [];
            }
            catch (OperationCanceledException) { return ValueResult<List<string>>.FromCancellation(); }
            catch (Exception ex)
            {
                return ValueResult<List<string>>.FromError("Unable to read client storage keys.", $"Error reading client storage keys: {ex}");
            }
        }

        public async ValueTask<ValueResult<TType?>> GetAsync<TType>(string scope, string key, CancellationToken ct = default)
        {
            try
            {
                var storageKey = CreateKey(scope, key);
                var module = await _moduleTask.Value.ConfigureAwait(false);
                var json = await module.InvokeAsync<string?>("getItem", ct, _storageName, storageKey).ConfigureAwait(false);

                if (json is null) return ValueResult<TType?>.FromValue(default);
                return ValueResult<TType?>.FromValue(JsonSerializer.Deserialize<TType>(json, _serializerOptions));
            }
            catch (OperationCanceledException) { return ValueResult<TType?>.FromCancellation(); }
            catch (Exception ex)
            {
                return ValueResult<TType?>.FromError("Unable to read client storage.", $"Error reading client storage [{scope}.{key}]: {ex}");
            }
        }

        public async ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default)
        {
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(scope);

                var module = await _moduleTask.Value.ConfigureAwait(false);
                var keys = await module.InvokeAsync<string[]>("getKeys", ct, _storageName, scope).ConfigureAwait(false);
                return keys?.ToList() ?? [];
            }
            catch (OperationCanceledException) { return ValueResult<List<string>>.FromCancellation(); }
            catch (Exception ex)
            {
                return ValueResult<List<string>>.FromError("Unable to read client storage keys.", $"Error reading client storage keys for scope [{scope}]: {ex}");
            }
        }

        public async ValueTask<Result> RemoveAsync(string scope, string key)
        {
            try
            {
                var storageKey = CreateKey(scope, key);
                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("removeItem", CancellationToken.None, _storageName, storageKey).ConfigureAwait(false);
                return Result.Success;
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (Exception ex)
            {
                return Result.FromError("Unable to remove client storage value.", $"Error removing client storage [{scope}.{key}]: {ex}");
            }
        }

        public async ValueTask<Result> RemoveAsync(string scope)
        {
            try
            {
                var keysResult = await GetKeysAsync(scope).ConfigureAwait(false);
                if (keysResult.IsCanceled) return Result.FromCancellation();
                if (keysResult.TryGetFailure(out var failure)) return Result.FromError(failure);
                if (!keysResult.TryGetSuccess(out var keys) || keys.Count == 0) return Result.Success;

                var module = await _moduleTask.Value.ConfigureAwait(false);
                foreach (var storageKey in keys)
                {
                    await module.InvokeVoidAsync("removeItem", CancellationToken.None, _storageName, storageKey).ConfigureAwait(false);
                }
                return Result.Success;
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (Exception ex)
            {
                return Result.FromError("Unable to remove client storage scope.", $"Error removing client storage scope [{scope}]: {ex}");
            }
        }

        public async ValueTask<Result> SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default)
        {
            try
            {
                var storageKey = CreateKey(scope, key);
                var json = JsonSerializer.Serialize(value, _serializerOptions);

                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.InvokeVoidAsync("setItem", ct, _storageName, storageKey, json).ConfigureAwait(false);
                return Result.Success;
            }
            catch (OperationCanceledException) { return Result.FromCancellation(); }
            catch (Exception ex)
            {
                return Result.FromError("Unable to write client storage.", $"Error writing client storage [{scope}.{key}]: {ex}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_moduleTask.IsValueCreated) return;

                var module = await _moduleTask.Value.ConfigureAwait(false);
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (JSDisconnectedException)
            {
                // Ignore circuit break
            }

            GC.SuppressFinalize(this);
        }

        private static string CreateKey(string scope, string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            return $"{scope}.{key}";
        }
    }
}
