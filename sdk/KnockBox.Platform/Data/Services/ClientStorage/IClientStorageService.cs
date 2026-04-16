namespace KnockBox.Data.Services.ClientStorage
{
    public readonly record struct StorageResult<TValue>(TValue Value, bool Exists);

    /// <summary>
    /// A service that stores data on the client.
    /// </summary>
    public interface IClientStorageService : IAsyncDisposable
    {
        /// <summary>
        /// Gets the value stored at the key.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="scope"></param>
        /// <param name="key"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<TType> GetAsync<TType>(string scope, string key, CancellationToken ct = default);

        /// <summary>
        /// Gets all the keys in the scope.
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<List<string>> GetKeysAsync(string scope, CancellationToken ct = default);

        /// <summary>
        /// Gets all the keys in the client storage.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<List<string>> GetAllKeysAsync(CancellationToken ct = default);

        /// <summary>
        /// Sets the value in client storage.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="scope"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default);

        /// <summary>
        /// Removes the value at the key in client storage.
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        ValueTask RemoveAsync(string scope, string key);

        /// <summary>
        /// Removes all the values in the scope in client storage.
        /// </summary>
        /// <param name="scope"></param>
        /// <returns></returns>
        ValueTask RemoveAsync(string scope);

        /// <summary>
        /// Deletes all data across all scopes in the client storage.
        /// </summary>
        /// <returns></returns>
        ValueTask ClearAsync();
    }
}
