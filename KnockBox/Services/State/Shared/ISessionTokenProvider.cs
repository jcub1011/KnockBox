using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Shared;

/// <summary>
/// A service used to get the session token for this user.
/// </summary>
public interface ISessionTokenProvider
{
    /// <summary>
    /// Gets the session token that belongs to this user.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask<ValueResult<SessionToken>> GetSessionTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates and returns a new session token for this user.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    ValueTask<ValueResult<SessionToken>> ProvisionNewTokenAsync(CancellationToken ct = default);
}
