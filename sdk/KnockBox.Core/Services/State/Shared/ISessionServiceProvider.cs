using KnockBox.Core.Primitives.Returns;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Core.Services.State.Shared;

/// <summary>
/// The token used associate services with a particular session.
/// </summary>
public readonly struct SessionToken : IEquatable<SessionToken>
{
    public readonly string Token = string.Empty;
    public readonly int HashCode = 0;

    public bool Equals(SessionToken other) => Token == other.Token;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SessionToken other && Equals(other);
    public override int GetHashCode() => HashCode;

    public SessionToken(Guid guid)
    {
        Token = guid.ToString();
        HashCode = Token.GetHashCode();
    }

    public SessionToken(string token)
    {
        Token = token;
        HashCode = Token.GetHashCode();
    }

    public static bool operator ==(SessionToken left, SessionToken right) => left.Equals(right);

    public static bool operator !=(SessionToken left, SessionToken right) => !(left == right);
}

/// <typeparam name="TService"></typeparam>
/// <param name="SessionToken">The session token for this registration.</param>
/// <param name="Service">The requested service.</param>
/// <param name="LifecycleToken">The token used to track if the service is in use.</param>
public readonly record struct ServiceRegistration<TService>(SessionToken SessionToken, TService Service, IDisposable LifecycleToken);

/// <summary>
/// A service provider that scopes to a <see cref="SessionToken"/> rather than a circuit lifecycle.
/// </summary>
public interface ISessionServiceProvider
{
    /// <summary>
    /// Gets or creates a service scoped to the session token.
    /// Disposes the returned service after 1 minute of the lifecycle token being disposed.
    /// Service won't be disposed as long as one lifecycle token exists.
    /// </summary>
    /// <typeparam name="TService"></typeparam>
    /// <param name="sessionToken"></param>
    /// <returns></returns>
    ValueResult<ServiceRegistration<TService>> GetService<TService>(SessionToken sessionToken);
}
