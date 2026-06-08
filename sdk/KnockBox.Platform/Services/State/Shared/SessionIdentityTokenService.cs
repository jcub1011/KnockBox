using System.Text.Json;
using System.Text.Json.Serialization;
using KnockBox.Core.Services.State.Shared;
using Microsoft.AspNetCore.DataProtection;

namespace KnockBox.Services.State.Shared;

/// <summary>
/// Default <see cref="ISessionIdentityTokenService"/>: protects a server-minted
/// user id with ASP.NET Core Data Protection so the resulting string is
/// tamper-proof and requires no server-side store. The protected payload is a
/// small versioned JSON document; only this server (its data-protection keys) can
/// read or forge it.
/// </summary>
internal sealed class SessionIdentityTokenService : ISessionIdentityTokenService
{
    /// <summary>
    /// Purpose string scoping the data protector. Versioned so a future payload
    /// change can rotate the purpose (invalidating old tokens) deliberately.
    /// </summary>
    private const string ProtectorPurpose = "KnockBox.Session.IdentityToken.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IDataProtector _protector;

    public SessionIdentityTokenService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(ProtectorPurpose);

    public string Issue()
    {
        var payload = new TokenPayload(
            Version: 1,
            UserId: Guid.CreateVersion7(),
            IssuedAtUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return _protector.Protect(json);
    }

    public bool TryResolve(string? token, out Guid userId)
    {
        userId = default;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        string json;
        try
        {
            json = _protector.Unprotect(token);
        }
        catch
        {
            // Tampered, foreign (different purpose/keys), or malformed ciphertext.
            return false;
        }

        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null || payload.UserId == Guid.Empty)
            return false;

        userId = payload.UserId;
        return true;
    }

    private sealed record TokenPayload(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("uid")] Guid UserId,
        [property: JsonPropertyName("iat")] long IssuedAtUnix);
}
