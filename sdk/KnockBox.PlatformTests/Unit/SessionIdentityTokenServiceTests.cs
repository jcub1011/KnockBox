using KnockBox.Services.State.Shared;
using Microsoft.AspNetCore.DataProtection;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Coverage for <see cref="SessionIdentityTokenService"/> — the per-tab identity
/// token the circuit-free WASM client presents on its hub handshake. Uses an
/// ephemeral data-protection provider so no key directory is touched.
/// </summary>
[TestClass]
public sealed class SessionIdentityTokenServiceTests
{
    private static SessionIdentityTokenService Build(IDataProtectionProvider? provider = null)
        => new(provider ?? new EphemeralDataProtectionProvider());

    [TestMethod]
    public void Issue_ThenResolve_RoundTripsTheSameUserId()
    {
        var svc = Build();

        var token = svc.Issue();

        Assert.IsTrue(svc.TryResolve(token, out var userId));
        Assert.AreNotEqual(Guid.Empty, userId);
    }

    [TestMethod]
    public void Issue_TwoTokens_HaveDistinctUserIds()
    {
        var svc = Build();

        Assert.IsTrue(svc.TryResolve(svc.Issue(), out var first));
        Assert.IsTrue(svc.TryResolve(svc.Issue(), out var second));

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not-a-real-token")]
    public void TryResolve_GarbageOrEmpty_ReturnsFalse(string? token)
    {
        var svc = Build();

        Assert.IsFalse(svc.TryResolve(token, out var userId));
        Assert.AreEqual(Guid.Empty, userId);
    }

    [TestMethod]
    public void TryResolve_TamperedToken_ReturnsFalse()
    {
        var svc = Build();
        var token = svc.Issue();
        // Flip the final character to corrupt the ciphertext/MAC.
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.IsFalse(svc.TryResolve(tampered, out _));
    }

    [TestMethod]
    public void TryResolve_TokenFromDifferentProvider_ReturnsFalse()
    {
        // A token minted under one set of keys must not resolve under another —
        // proves the signature is bound to this server's data-protection keys.
        var issuer = Build();
        var foreign = Build();

        var token = issuer.Issue();

        Assert.IsFalse(foreign.TryResolve(token, out _));
    }
}
