using System.Reflection;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.PlatformTests.Unit;

/// <summary>
/// Phase 4 verification: the public <see cref="User"/> API is locked down
/// (internal ctor, internal-settable <c>Name</c>), and <see cref="IUserService"/>
/// is the only path to mutate the current user's name — with trim+cap,
/// isolated event fan-out, and idempotence.
/// </summary>
[TestClass]
public sealed class UserServiceTests
{
    [TestMethod]
    public void User_Constructor_IsInternal()
    {
        // The (string, string) ctor must exist but be non-public so external
        // callers can't bypass IUserService to mint arbitrary User instances.
        var ctor = typeof(User).GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(string)],
            modifiers: null);

        Assert.IsNotNull(ctor, "User(string, string) ctor must exist.");
        Assert.IsFalse(ctor.IsPublic, "User ctor must not be public.");
        Assert.IsTrue(ctor.IsAssembly, "User ctor must be internal (assembly).");
    }

    [TestMethod]
    public void User_NameSetter_IsInternal()
    {
        var setter = typeof(User).GetProperty(nameof(User.Name))?.SetMethod;

        Assert.IsNotNull(setter, "User.Name must have a setter.");
        Assert.IsFalse(setter.IsPublic, "User.Name setter must not be public.");
        Assert.IsTrue(setter.IsAssembly, "User.Name setter must be internal (assembly).");
    }

    [TestMethod]
    public void User_IdSetter_DoesNotExist()
    {
        var setter = typeof(User).GetProperty(nameof(User.Id))?.SetMethod;
        Assert.IsNull(setter, "User.Id must be read-only.");
    }

    [TestMethod]
    public void UserFactory_Create_BuildsUserWithSuppliedValues()
    {
        var user = UserFactory.Create("Alice", "alice-id");
        Assert.AreEqual("Alice", user.Name);
        Assert.AreEqual("alice-id", user.Id);
    }

    [TestMethod]
    public void UserFactory_Create_TrimsAndCapsName()
    {
        // Mirrors SetCurrentUserName: trim whitespace, cap at 12 chars.
        var trimmed = UserFactory.Create("   Bob   ", "id1");
        Assert.AreEqual("Bob", trimmed.Name);

        var capped = UserFactory.Create("ThisNameIsWayTooLong", "id2");
        Assert.AreEqual("ThisNameIsWa", capped.Name);
        Assert.AreEqual(12, capped.Name.Length);
    }

    [TestMethod]
    public void UserFactory_CreateUnchecked_LeavesNameAsIs()
    {
        var user = UserFactory.CreateUnchecked("   ThisNameIsWayTooLong   ", "id");
        Assert.AreEqual("   ThisNameIsWayTooLong   ", user.Name);
    }

    [TestMethod]
    public async Task SetCurrentUserName_TrimsWhitespace()
    {
        var (service, _) = await NewInitializedServiceAsync();

        service.SetCurrentUserName("  Bob  ");

        Assert.AreEqual("Bob", service.CurrentUser!.Name);
    }

    [TestMethod]
    public async Task SetCurrentUserName_CapsAtTwelveCharacters()
    {
        var (service, _) = await NewInitializedServiceAsync();

        service.SetCurrentUserName("abcdefghijklmnop"); // 16 chars

        Assert.AreEqual(12, service.CurrentUser!.Name.Length);
        Assert.AreEqual("abcdefghijkl", service.CurrentUser.Name);
    }

    [TestMethod]
    public async Task SetCurrentUserName_FiresUserNameChangedWithPreviousAndNewName()
    {
        var (service, _) = await NewInitializedServiceAsync();

        UserNameChangedArgs? observed = null;
        service.UserNameChanged += args => observed = args;

        var previous = service.CurrentUser!.Name;
        service.SetCurrentUserName("Alice");

        Assert.IsNotNull(observed);
        Assert.AreEqual(previous, observed.PreviousName);
        Assert.AreEqual("Alice", observed.NewName);
    }

    [TestMethod]
    public async Task SetCurrentUserName_IsNoOpWhenTrimmedValueMatchesCurrent()
    {
        var (service, _) = await NewInitializedServiceAsync();
        service.SetCurrentUserName("Alice");

        int fires = 0;
        service.UserNameChanged += _ => fires++;

        service.SetCurrentUserName("  Alice  ");

        Assert.AreEqual(0, fires, "Setting the same name (after trim) must not re-fire UserNameChanged.");
    }

    [TestMethod]
    public async Task SetCurrentUserName_PersistsToLocalStorage()
    {
        var (service, storage) = await NewInitializedServiceAsync();

        service.SetCurrentUserName("Alice");

        // SaveNameAsync is fire-and-forget. Give the scheduler a beat.
        var saved = await WaitFor(() => storage.GetValue("user", "name") == "Alice");
        Assert.IsTrue(saved, $"Expected localStorage['user']['name'] == 'Alice', got '{storage.GetValue("user", "name")}'.");
    }

    [TestMethod]
    public async Task UserNameChanged_ThrowingHandler_DoesNotShortCircuitInvocationList()
    {
        var (service, _) = await NewInitializedServiceAsync();

        bool goodHandlerRan = false;
        service.UserNameChanged += _ => throw new InvalidOperationException("boom");
        service.UserNameChanged += _ => goodHandlerRan = true;

        // Must not propagate.
        service.SetCurrentUserName("Alice");

        Assert.IsTrue(goodHandlerRan, "A throwing subscriber must not short-circuit the rest of the invocation list.");
    }

    [TestMethod]
    public void SetCurrentUserName_WhenCurrentUserIsNull_IsNoOp()
    {
        var service = NewService(out _);
        Assert.IsNull(service.CurrentUser);

        // Must not throw.
        service.SetCurrentUserName("Anybody");

        Assert.IsNull(service.CurrentUser);
    }

    [TestMethod]
    public async Task InitializeCurrentUserAsync_StoredNameLongerThanCap_IsTruncated()
    {
        // A name persisted by an older version (or hand-edited localStorage)
        // could exceed the 12-char cap. Construction must apply the same
        // trim+cap normalization as UserFactory.Create / SetCurrentUserName
        // so CurrentUser.Name honors the v1 invariant regardless of source.
        var service = NewService(out var storage);
        await storage.SetAsync("user", "name", "abcdefghijklmnop"); // 16 chars

        await service.InitializeCurrentUserAsync();

        Assert.IsNotNull(service.CurrentUser);
        Assert.AreEqual(12, service.CurrentUser!.Name.Length);
        Assert.AreEqual("abcdefghijkl", service.CurrentUser.Name);
    }

    [TestMethod]
    public async Task SetCurrentUserName_AfterDispose_IsNoOp()
    {
        var (service, storage) = await NewInitializedServiceAsync();
        storage.Reset();

        service.Dispose();
        service.SetCurrentUserName("Alice");

        // Pre-dispose name still stands; no write attempted.
        Assert.AreNotEqual("Alice", service.CurrentUser!.Name);
        Assert.IsNull(storage.GetValue("user", "name"));
    }

    // ─── fixtures ───────────────────────────────────────────────────────────

    private static UserService NewService(out FakeLocalStorage storage)
    {
        storage = new FakeLocalStorage();
        var tokenProvider = new FakeSessionTokenProvider();
        return new UserService(storage, tokenProvider, NullLogger<UserService>.Instance);
    }

    private static async Task<(UserService service, FakeLocalStorage storage)> NewInitializedServiceAsync()
    {
        var service = NewService(out var storage);
        await service.InitializeCurrentUserAsync();
        return (service, storage);
    }

    private static async Task<bool> WaitFor(Func<bool> predicate, int timeoutMs = 1000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount >= deadline) return false;
            await Task.Delay(10);
        }
        return true;
    }

    private sealed class FakeLocalStorage : ILocalStorageService
    {
        private readonly Dictionary<(string scope, string key), object?> _store = new();
        private readonly Lock _lock = new();

        public string? GetValue(string scope, string key)
        {
            lock (_lock)
            {
                return _store.TryGetValue((scope, key), out var v) ? v as string : null;
            }
        }

        public void Reset()
        {
            lock (_lock) _store.Clear();
        }

        public ValueTask<ValueResult<TType?>> GetAsync<TType>(string scope, string key, CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (_store.TryGetValue((scope, key), out var v) && v is TType t)
                    return new(ValueResult<TType?>.FromValue(t));
            }
            return new(ValueResult<TType?>.FromValue(default));
        }

        public ValueTask<Result> SetAsync<TType>(string scope, string key, TType value, CancellationToken ct = default)
        {
            lock (_lock) _store[(scope, key)] = value;
            return new(Result.Success);
        }

        public ValueTask<ValueResult<List<string>>> GetKeysAsync(string scope, CancellationToken ct = default) => new(ValueResult<List<string>>.FromValue([]));
        public ValueTask<ValueResult<List<string>>> GetAllKeysAsync(CancellationToken ct = default) => new(ValueResult<List<string>>.FromValue([]));
        public ValueTask<Result> RemoveAsync(string scope, string key) => new(Result.Success);
        public ValueTask<Result> RemoveAsync(string scope) => new(Result.Success);
        public ValueTask<Result> ClearAsync() => new(Result.Success);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSessionTokenProvider : ISessionTokenProvider
    {
        public ValueTask<ValueResult<SessionToken>> GetSessionTokenAsync(CancellationToken ct = default)
            => new(new SessionToken(Guid.NewGuid()));

        public ValueTask<ValueResult<SessionToken>> ProvisionNewTokenAsync(CancellationToken ct = default)
            => new(new SessionToken(Guid.NewGuid()));
    }
}
