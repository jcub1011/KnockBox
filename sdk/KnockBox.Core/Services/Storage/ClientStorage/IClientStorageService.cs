using KnockBox.Core.Primitives.Returns;

namespace KnockBox.Core.Services.Storage.ClientStorage
{
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
        /// <returns>
        /// A success result whose value is the stored item, or <c>null</c> when no value
        /// is stored at the key (a miss is not a failure); a failure result when the
        /// underlying read or deserialization fails; or a cancellation result.
        /// </returns>
        ValueTask<ValueResult<TType?>> GetAsync<TType>(string scope, string key, CancellationToken ct = default);

        /// <summary>
        /// Gets all the keys in the scope.
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default);

        /// <summary>
        /// Gets all the keys in the client storage.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default);

        /// <summary>
        /// Sets the value in client storage.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="scope"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<Result> SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default);

        /// <summary>
        /// Removes the value at the key in client storage.
        /// </summary>
        /// <param name="scope"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        ValueTask<Result> RemoveAsync(string scope, string key);

        /// <summary>
        /// Removes all the values in the scope in client storage.
        /// </summary>
        /// <param name="scope"></param>
        /// <returns></returns>
        ValueTask<Result> RemoveAsync(string scope);

        /// <summary>
        /// Deletes all data across all scopes in the client storage.
        /// </summary>
        /// <returns></returns>
        ValueTask<Result> ClearAsync();
    }

    /// <summary>Marker interface for browser <c>sessionStorage</c>-backed client storage.</summary>
    public interface ISessionStorageService : IClientStorageService { }

    /// <summary>Marker interface for browser <c>localStorage</c>-backed client storage.</summary>
    public interface ILocalStorageService : IClientStorageService { }
}
