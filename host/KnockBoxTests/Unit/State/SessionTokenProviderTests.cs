using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Services.State.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace KnockBox.Tests.Unit.State;

/// <summary>
/// Coverage for <see cref="SessionTokenProvider"/> after identity unification: it
/// resolves the circuit's <see cref="SessionToken"/> from the SAME server-signed
/// per-tab token (in <c>sessionStorage</c>) the WASM hub reads, minting one when
/// absent or tampered, and always surfacing a parseable-GUID token so the
/// <c>UserService</c> contract holds. Uses in-process fakes for <see cref="IJSRuntime"/>
/// and <see cref="ISessionIdentityTokenService"/> so the test stays deterministic
/// and free of a real Data-Protection key directory.
/// </summary>
[TestClass]
public class SessionTokenProviderTests : ISessionTokenProviderContractTests<SessionTokenProvider>
{
    private const string StorageKey = "KnockBox.SessionToken";

    private FakeJsRuntime _js = null!;
    private FakeIdentityTokens _identityTokens = null!;

    [TestInitialize]
    public void Setup()
    {
        _js = new FakeJsRuntime();
        _identityTokens = new FakeIdentityTokens();
    }

    protected override SessionTokenProvider CreateProvider()
        => new(_js, _identityTokens, NullLogger<SessionTokenProvider>.Instance);

    [TestMethod]
    public async Task GetSessionTokenAsync_NoStoredToken_MintsSignedTokenAndWritesBack()
    {
        var provider = CreateProvider();

        var result = await provider.GetSessionTokenAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, _identityTokens.IssueCalls, "Should mint exactly one token.");
        Assert.AreEqual(1, _js.SetItemCalls, "Should persist the minted token once.");
        Assert.IsTrue(_js.TryRead(StorageKey, out var stored));
        // The persisted value is the opaque signed token; the resolved id is the GUID.
        Assert.AreNotEqual(stored, result.Value.Token, "Stored token should be the opaque signed value.");
        Assert.IsTrue(_identityTokens.TryResolve(stored, out var storedId));
        Assert.AreEqual(storedId.ToString(), result.Value.Token);
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_ValidStoredToken_ResolvesWithoutMinting()
    {
        var existingId = Guid.NewGuid();
        _js.Seed(StorageKey, FakeIdentityTokens.Sign(existingId));
        var provider = CreateProvider();

        var result = await provider.GetSessionTokenAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(existingId.ToString(), result.Value.Token);
        Assert.AreEqual(0, _identityTokens.IssueCalls, "A valid stored token must not be re-minted.");
        Assert.AreEqual(0, _js.SetItemCalls, "A valid stored token must not be re-written.");
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_TamperedStoredToken_MintsFresh()
    {
        _js.Seed(StorageKey, "not-a-valid-signed-token");
        var provider = CreateProvider();

        var result = await provider.GetSessionTokenAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, _identityTokens.IssueCalls, "A tampered token must be replaced.");
        Assert.AreEqual(1, _js.SetItemCalls);
        Assert.IsTrue(Guid.TryParse(result.Value.Token, out _));
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_ReturnedToken_IsParseableGuid()
    {
        var provider = CreateProvider();

        var result = await provider.GetSessionTokenAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(Guid.TryParse(result.Value.Token, out _),
            "UserService parses the token as a Guid; the provider must keep returning a GUID string.");
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_MultipleCalls_DoNotReReadOrReMint()
    {
        var provider = CreateProvider();

        await provider.GetSessionTokenAsync();
        await provider.GetSessionTokenAsync();
        await provider.GetSessionTokenAsync();

        Assert.AreEqual(1, _js.GetItemCalls, "Token is cached after the first resolution.");
        Assert.AreEqual(1, _identityTokens.IssueCalls);
        Assert.AreEqual(1, _js.SetItemCalls);
    }

    [TestMethod]
    public async Task ProvisionNewTokenAsync_MintsNewSignedToken_OverwritesStorage()
    {
        var oldId = Guid.NewGuid();
        _js.Seed(StorageKey, FakeIdentityTokens.Sign(oldId));
        var provider = CreateProvider();

        var result = await provider.ProvisionNewTokenAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.AreNotEqual(oldId.ToString(), result.Value.Token);
        Assert.IsTrue(_js.TryRead(StorageKey, out var stored));
        Assert.IsTrue(_identityTokens.TryResolve(stored, out var newId));
        Assert.AreEqual(newId.ToString(), result.Value.Token);
    }

    [TestMethod]
    public async Task GetSessionTokenAsync_JsInteropThrows_ReturnsFailure()
    {
        _js.ThrowOnGet = true;
        var provider = CreateProvider();

        var result = await provider.GetSessionTokenAsync();

        Assert.IsTrue(result.TryGetFailure(out _),
            "A JS-interop failure must surface as a ResultError so UserService can retry/fall back.");
    }

    /// <summary>
    /// In-memory <c>sessionStorage</c> over <see cref="IJSRuntime"/>. Only the two
    /// calls the provider makes (<c>getItem</c>/<c>setItem</c>) are supported.
    /// </summary>
    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string?> _store = new(StringComparer.Ordinal);

        public int GetItemCalls { get; private set; }
        public int SetItemCalls { get; private set; }
        public bool ThrowOnGet { get; set; }

        public void Seed(string key, string? value) => _store[key] = value;
        public bool TryRead(string key, out string? value) => _store.TryGetValue(key, out value);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => Handle<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => Handle<TValue>(identifier, args);

        private ValueTask<TValue> Handle<TValue>(string identifier, object?[]? args)
        {
            switch (identifier)
            {
                case "sessionStorage.getItem":
                    GetItemCalls++;
                    if (ThrowOnGet) throw new InvalidOperationException("JS interop unavailable.");
                    _store.TryGetValue((string)args![0]!, out var val);
                    return new ValueTask<TValue>((TValue)(object?)val!);
                case "sessionStorage.setItem":
                    SetItemCalls++;
                    _store[(string)args![0]!] = (string?)args[1];
                    return new ValueTask<TValue>(default(TValue)!);
                default:
                    throw new NotSupportedException(identifier);
            }
        }
    }

    /// <summary>
    /// Deterministic stand-in for <see cref="ISessionIdentityTokenService"/>: an
    /// "issued" token is <c>signed-{guid}</c>, resolvable back to that guid. Good
    /// enough to exercise the provider's mint / resolve / write-back paths without
    /// the real Data-Protection signer (which is internal to KnockBox.Platform).
    /// </summary>
    private sealed class FakeIdentityTokens : ISessionIdentityTokenService
    {
        private const string Prefix = "signed-";

        public int IssueCalls { get; private set; }

        public static string Sign(Guid id) => Prefix + id;

        public string Issue()
        {
            IssueCalls++;
            return Sign(Guid.NewGuid());
        }

        public bool TryResolve(string? token, out Guid userId)
        {
            userId = Guid.Empty;
            if (token is null || !token.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            return Guid.TryParse(token.AsSpan(Prefix.Length), out userId);
        }
    }
}
