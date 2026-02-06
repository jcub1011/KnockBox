using Microsoft.JSInterop;
using System.Text.Json;

namespace KnockBox.Data.Services.ClientStorage
{
    public class BrowserStorageService : IClientStorageService
    {
        private readonly string _storageName;
        private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
        private readonly Lazy<Task<IJSObjectReference>> _moduleTask;

        public BrowserStorageService(IJSRuntime jsRuntime, string storageName)
        {
            ArgumentNullException.ThrowIfNull(jsRuntime);
            _storageName = storageName;

            _moduleTask = new Lazy<Task<IJSObjectReference>>(() =>
                jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/localStorageService.js").AsTask());
        }

        public async ValueTask ClearAsync()
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.InvokeVoidAsync("clear", CancellationToken.None, _storageName).ConfigureAwait(false);
        }

        public async ValueTask<List<string>> GetAllKeysAsync(CancellationToken ct = default)
        {
            var module = await _moduleTask.Value.ConfigureAwait(false);
            var keys = await module.InvokeAsync<string[]>("getAllKeys", ct, _storageName).ConfigureAwait(false);
            return keys?.ToList() ?? [];
        }

        public async ValueTask<TType> GetAsync<TType>(string scope, string key, CancellationToken ct = default)
        {
            var storageKey = CreateKey(scope, key);
            var module = await _moduleTask.Value.ConfigureAwait(false);
            var json = await module.InvokeAsync<string?>("getItem", ct, _storageName, storageKey).ConfigureAwait(false);

            if (json is null) return default!;
            return JsonSerializer.Deserialize<TType>(json, _serializerOptions)!;
        }

        public async ValueTask<List<string>> GetKeysAsync(string scope, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scope);

            var module = await _moduleTask.Value.ConfigureAwait(false);
            var keys = await module.InvokeAsync<string[]>("getKeys", ct, _storageName, scope).ConfigureAwait(false);
            return keys?.ToList() ?? [];
        }

        public async ValueTask RemoveAsync(string scope, string key)
        {
            var storageKey = CreateKey(scope, key);
            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.InvokeVoidAsync("removeItem", CancellationToken.None, _storageName, storageKey).ConfigureAwait(false);
        }

        public async ValueTask RemoveAsync(string scope)
        {
            var keys = await GetKeysAsync(scope).ConfigureAwait(false);
            if (keys.Count == 0) return;

            var module = await _moduleTask.Value.ConfigureAwait(false);
            foreach (var storageKey in keys)
            {
                await module.InvokeVoidAsync("removeItem", CancellationToken.None, _storageName, storageKey).ConfigureAwait(false);
            }
        }

        public async ValueTask SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default)
        {
            var storageKey = CreateKey(scope, key);
            var json = JsonSerializer.Serialize(value, _serializerOptions);

            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.InvokeVoidAsync("setItem", ct, _storageName, storageKey, json).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_moduleTask.IsValueCreated) return;

            var module = await _moduleTask.Value.ConfigureAwait(false);
            await module.DisposeAsync().ConfigureAwait(false);

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
