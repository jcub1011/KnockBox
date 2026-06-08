using System.Text.Json;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.ClientStorage;

namespace KnockBox.Tests.Unit.Services.Storage;

[TestClass]
public sealed class ScopedClientStorageServiceTests
{
    [TestMethod]
    public async Task SetAsync_StoresUnderRoutePrefixedScope()
    {
        var inner = new InMemoryClientStorage();
        var scoped = new ScopedClientStorageService(inner, "card-counter");

        await scoped.SetAsync("settings", "value", 42);

        Assert.IsTrue(inner.Store.ContainsKey("card-counter::settings.value"),
            "Stored key should be namespaced by the route.");
    }

    [TestMethod]
    public async Task GetAsync_RoundTripsThroughScopedKey()
    {
        var inner = new InMemoryClientStorage();
        var scoped = new ScopedClientStorageService(inner, "card-counter");

        await scoped.SetAsync("settings", "value", new Payload(7, "hi"));
        var result = await scoped.GetAsync<Payload>("settings", "value");

        Assert.IsTrue(result.TryGetSuccess(out var payload));
        Assert.AreEqual(7, payload!.Number);
        Assert.AreEqual("hi", payload.Text);
    }

    [TestMethod]
    public async Task GetAllKeysAsync_ReturnsOnlyOwnNamespace_WithPrefixStripped()
    {
        var inner = new InMemoryClientStorage();
        // Pre-seed host data and another plugin's data the wrapper must not surface.
        inner.Store["user.name"] = "\"alice\"";
        inner.Store["other-plugin::settings.value"] = "1";

        var scoped = new ScopedClientStorageService(inner, "card-counter");
        await scoped.SetAsync("settings", "value", 1);
        await scoped.SetAsync("history", "entry", 2);

        var result = await scoped.GetAllKeysAsync();

        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.HasCount(2, keys);
        Assert.Contains("settings.value", keys);
        Assert.Contains("history.entry", keys);
        Assert.DoesNotContain("user.name", keys);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesOnlyOwnNamespace_LeavesHostAndOtherPlugins()
    {
        var inner = new InMemoryClientStorage();
        inner.Store["user.name"] = "\"alice\"";                       // host
        inner.Store["SessionTokenProvider.token"] = "\"abc\"";         // host
        inner.Store["other-plugin::settings.value"] = "1";            // another plugin

        var scoped = new ScopedClientStorageService(inner, "card-counter");
        await scoped.SetAsync("settings", "value", 1);
        await scoped.SetAsync("history", "entry", 2);

        var result = await scoped.ClearAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(inner.Store.ContainsKey("user.name"), "Host data must survive a plugin Clear.");
        Assert.IsTrue(inner.Store.ContainsKey("SessionTokenProvider.token"));
        Assert.IsTrue(inner.Store.ContainsKey("other-plugin::settings.value"));
        Assert.IsFalse(inner.Store.Keys.Any(k => k.StartsWith("card-counter::", StringComparison.Ordinal)),
            "All of the plugin's own keys should be cleared.");
    }

    [TestMethod]
    public async Task TwoRoutes_AreIsolated()
    {
        var inner = new InMemoryClientStorage();
        var a = new ScopedClientStorageService(inner, "alpha-chain");
        var b = new ScopedClientStorageService(inner, "codeword");

        await a.SetAsync("settings", "value", 1);
        await b.SetAsync("settings", "value", 2);

        var ra = await a.GetAsync<int>("settings", "value");
        var rb = await b.GetAsync<int>("settings", "value");

        Assert.IsTrue(ra.TryGetSuccess(out var va));
        Assert.IsTrue(rb.TryGetSuccess(out var vb));
        Assert.AreEqual(1, va);
        Assert.AreEqual(2, vb);
    }

    [TestMethod]
    public async Task RemoveAsync_ByScope_RemovesOnlyThatScopeWithinNamespace()
    {
        var inner = new InMemoryClientStorage();
        var scoped = new ScopedClientStorageService(inner, "operator");
        await scoped.SetAsync("settings", "a", 1);
        await scoped.SetAsync("settings", "b", 2);
        await scoped.SetAsync("history", "x", 3);

        await scoped.RemoveAsync("settings");

        Assert.IsFalse(inner.Store.ContainsKey("operator::settings.a"));
        Assert.IsFalse(inner.Store.ContainsKey("operator::settings.b"));
        Assert.IsTrue(inner.Store.ContainsKey("operator::history.x"),
            "Removing one scope must not touch another scope in the same namespace.");
    }

    [TestMethod]
    public async Task ClearAsync_RemovesKeysWhoseScopeContainsADot()
    {
        // Regression: ClearAsync must remove every key in the namespace even when
        // a scope itself contains '.', which a naive first-'.' split would
        // mis-parse and leave behind.
        var inner = new InMemoryClientStorage();
        var scoped = new ScopedClientStorageService(inner, "card-counter");
        await scoped.SetAsync("a.b", "value", 1);   // stored as "card-counter::a.b.value"
        await scoped.SetAsync("plain", "value", 2);

        var result = await scoped.ClearAsync();

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(inner.Store.Keys.Any(k => k.StartsWith("card-counter::", StringComparison.Ordinal)),
            "Every key in the namespace must be cleared, including dotted scopes.");
    }

    [TestMethod]
    public async Task GetKeysAsync_StripsRoutePrefix()
    {
        var inner = new InMemoryClientStorage();
        var scoped = new ScopedClientStorageService(inner, "spardle");
        await scoped.SetAsync("settings", "value", 1);

        var result = await scoped.GetKeysAsync("settings");

        Assert.IsTrue(result.TryGetSuccess(out var keys));
        Assert.HasCount(1, keys);
        Assert.AreEqual("settings.value", keys[0]);
    }

    private sealed record Payload(int Number, string Text);

    /// <summary>
    /// In-memory <see cref="IClientStorageService"/> mirroring the real
    /// browser-backed service: keys are stored as <c>"{scope}.{key}"</c>, the
    /// same physical layout the JS module uses, so the wrapper's prefixing and
    /// namespace filtering are exercised against realistic keys.
    /// </summary>
    private sealed class InMemoryClientStorage : IClientStorageService
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public Dictionary<string, string> Store { get; } = new(StringComparer.Ordinal);

        public ValueTask<ValueResult<T?>> GetAsync<T>(string scope, string key, CancellationToken ct = default)
        {
            if (!Store.TryGetValue($"{scope}.{key}", out var json))
                return new(ValueResult<T?>.FromValue(default));
            return new(ValueResult<T?>.FromValue(JsonSerializer.Deserialize<T>(json, Options)));
        }

        public ValueTask<Result> SetAsync<T>(string scope, string key, T value, CancellationToken ct = default)
        {
            Store[$"{scope}.{key}"] = JsonSerializer.Serialize(value, Options);
            return new(Result.Success);
        }

        public ValueTask<Result> RemoveAsync(string scope, string key)
        {
            Store.Remove($"{scope}.{key}");
            return new(Result.Success);
        }

        public ValueTask<Result> RemoveAsync(string scope)
        {
            var prefix = $"{scope}.";
            foreach (var k in Store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Store.Remove(k);
            return new(Result.Success);
        }

        public ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default)
        {
            var prefix = $"{scope}.";
            var keys = Store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            return new(ValueResult<List<string>>.FromValue(keys));
        }

        public ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default)
            => new(ValueResult<List<string>>.FromValue(Store.Keys.ToList()));

        public ValueTask<Result> ClearAsync()
        {
            Store.Clear();
            return new(Result.Success);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
