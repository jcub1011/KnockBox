using KnockBox.Core.Components.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Browser;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.CoreTests.Unit.Components.Shared;

[TestClass]
public sealed class LobbyPageBaseTests
{
    // A concrete game state we can register on a LobbyRegistration and feed to LobbyPageBase<TGameState>.
    private sealed class TestGameState(User host, ILogger logger) : AbstractGameState(host, logger) { }

    // A different game state type used to trigger the "state is not TGameState" branch.
    private sealed class OtherGameState(User host, ILogger logger) : AbstractGameState(host, logger) { }

    // Test page inheriting the SUT. Exposes protected lifecycle hooks for direct invocation without a renderer.
    private sealed class TestLobbyPage : LobbyPageBase<TestGameState>
    {
        public Task InvokeOnInitializedAsync() => OnInitializedAsync();
        public void InvokeOnAfterRender(bool firstRender) => OnAfterRender(firstRender);
        public Task InvokeOnAfterRenderAsync(bool firstRender) => OnAfterRenderAsync(firstRender);
        public IWakeLockService PublicWakeLockService => WakeLockService;

        public bool LobbyInitCalled { get; private set; }
        protected override Task OnLobbyInitializedAsync()
        {
            LobbyInitCalled = true;
            return Task.CompletedTask;
        }

        public Func<(Action Action, int Interval)>? TickFactory { get; set; }
        protected override bool TryGetHostTick(out Action action, out int tickInterval)
        {
            if (TickFactory is null)
            {
                action = null!;
                tickInterval = 0;
                return false;
            }
            var (a, i) = TickFactory();
            action = a;
            tickInterval = i;
            return true;
        }

        public bool LobbyDisposingCalled { get; private set; }
        protected override void OnLobbyDisposing() => LobbyDisposingCalled = true;

        public TestGameState? PublicGameState => GameState;
        public string PublicRoomCode => RoomCode;
    }

    private sealed class StubNavigationService : INavigationService
    {
        public int ToHomeCount { get; private set; }
        public string GameBaseRoute => "/room";
        public string GetHomeUri() => "/";
        public void ToHome() => ToHomeCount++;
        public string GetGameUri(LobbyRegistration r) => r.Uri;
        public string GetJoinUri(string code, bool fresh = false) => $"/join/{code}";
        public void ToGame(LobbyRegistration r) { }
    }

    private sealed class StubGameSessionService(UserRegistration? session) : IGameSessionService
    {
        private readonly UserRegistration? _session = session;
        public int LeaveCount { get; private set; }
        public bool LastLeaveNavigateHome { get; private set; }

        public bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession)
        {
            currentSession = _session;
            return _session is not null;
        }

        public Result SetCurrentSession(UserRegistration session) => Result.Success;
        public Result LeaveCurrentSession(bool navigateHome = true)
        {
            LeaveCount++;
            LastLeaveNavigateHome = navigateHome;
            return Result.Success;
        }
    }

    private sealed class StubUserService(User user) : IUserService
    {
        public User? CurrentUser { get; private set; } = user;
        public event Action? UserInitialized;
        public event Action<UserNameChangedArgs>? UserNameChanged;
        public Task InitializeCurrentUserAsync(CancellationToken ct = default)
        {
            UserInitialized?.Invoke();
            return Task.CompletedTask;
        }
        public Task ResetIdentityAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void SetCurrentUserName(string name)
        {
            if (CurrentUser is null) return;
            var previous = CurrentUser.Name;
            CurrentUser.Name = name;
            UserNameChanged?.Invoke(new UserNameChangedArgs(previous, name));
        }
    }

    private sealed class StubTickService : ITickService
    {
        public int TicksPerSecond => 60;
        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(1000.0 / 60);
        public int RegisterCount { get; private set; }
        public Action? LastCallback { get; private set; }
        public int LastInterval { get; private set; }
        public DisposeTracker? LastSubscription { get; private set; }

        public ValueResult<IDisposable> RegisterTickCallback(Action tickCallback, int tickInterval = 1)
        {
            RegisterCount++;
            LastCallback = tickCallback;
            LastInterval = tickInterval;
            LastSubscription = new DisposeTracker();
            return ValueResult<IDisposable>.FromValue(LastSubscription);
        }
    }

    private sealed class DisposeTracker : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class StubWakeLockService : IWakeLockService
    {
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public Queue<bool> AcquireResults { get; } = new();
        public ValueTask<bool> AcquireAsync(CancellationToken ct = default)
        {
            AcquireCount++;
            var result = AcquireResults.Count > 0 ? AcquireResults.Dequeue() : true;
            return ValueTask.FromResult(result);
        }
        public ValueTask ReleaseAsync()
        {
            ReleaseCount++;
            return ValueTask.CompletedTask;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static User MakeUser(string name = "Player") => new(name, Guid.NewGuid().ToString());

    private static LobbyRegistration MakeLobby(AbstractGameState state, string? obfuscatedCode = null)
    {
        obfuscatedCode ??= "abc-def";
        return new LobbyRegistration(
            lobbyCode: obfuscatedCode,
            lobbyUri: $"/room/test/{obfuscatedCode}",
            gameName: "Test",
            routeIdentifier: "test",
            state: state);
    }

    private static UserRegistration MakeUserRegistration(User user, AbstractGameState state, string? obfuscatedCode = null)
    {
        var lobby = MakeLobby(state, obfuscatedCode);
        return new UserRegistration(user, Mock.Of<IDisposable>(), lobby);
    }

    private static TestLobbyPage MakePage(
        string obfuscatedRoomCode,
        IUserService userService,
        IGameSessionService sessionService,
        INavigationService navigationService,
        ITickService? tickService = null,
        IWakeLockService? wakeLockService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sessionService);
        services.AddSingleton(navigationService);
        services.AddSingleton(userService);
        services.AddSingleton(tickService ?? new StubTickService());
        services.AddSingleton(wakeLockService ?? new StubWakeLockService());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        var page = new TestLobbyPage();
        var provider = services.BuildServiceProvider();

        // Blazor's [Inject] resolution happens via the renderer in real usage. Since we instantiate
        // without a renderer, wire the injected properties by reflection — this is the standard
        // workaround for unit-testing ComponentBase derivatives without bunit.
        foreach (var prop in typeof(TestLobbyPage).GetProperties(System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
        {
            var inject = Attribute.IsDefined(prop, typeof(InjectAttribute));
            if (!inject) continue;
            var svc = provider.GetService(prop.PropertyType);
            if (svc is not null) prop.SetValue(page, svc);
        }

        // Bind [Parameter] ObfuscatedRoomCode
        var paramProp = typeof(TestLobbyPage).GetProperty(nameof(TestLobbyPage.ObfuscatedRoomCode));
        paramProp!.SetValue(page, obfuscatedRoomCode);

        return page;
    }

    // ── OnInitializedAsync early-return paths ────────────────────────────────

    [TestMethod]
    public async Task OnInitializedAsync_NoSession_RedirectsHome()
    {
        var user = MakeUser();
        var nav = new StubNavigationService();
        var session = new StubGameSessionService(null);
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(1, nav.ToHomeCount);
        Assert.IsFalse(page.LobbyInitCalled, "LobbyInit should not run when session is missing.");
    }

    [TestMethod]
    public async Task OnInitializedAsync_UriMismatch_RedirectsHome()
    {
        var user = MakeUser();
        var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state, obfuscatedCode: "different-code");

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(1, nav.ToHomeCount);
        Assert.IsFalse(page.LobbyInitCalled);
    }

    [TestMethod]
    public async Task OnInitializedAsync_WrongStateType_RedirectsHome()
    {
        var user = MakeUser();
        var wrongState = new OtherGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, wrongState);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(1, nav.ToHomeCount);
        Assert.IsFalse(page.LobbyInitCalled);
    }

    [TestMethod]
    public async Task OnInitializedAsync_DisposedState_RedirectsHome()
    {
        var user = MakeUser();
        var state = new TestGameState(user, NullLogger.Instance);
        state.Dispose();
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(1, nav.ToHomeCount);
        Assert.IsFalse(page.LobbyInitCalled);
    }

    // ── OnInitializedAsync happy path ────────────────────────────────────────

    [TestMethod]
    public async Task OnInitializedAsync_HappyPath_WiresStateAndCallsLobbyInit()
    {
        var user = MakeUser();
        using var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(0, nav.ToHomeCount);
        Assert.IsTrue(page.LobbyInitCalled);
        Assert.AreSame(state, page.PublicGameState);
        Assert.AreEqual("abc-def", page.PublicRoomCode);
    }

    // ── Host tick registration ───────────────────────────────────────────────

    [TestMethod]
    public async Task OnInitializedAsync_AsHost_RegistersTickCallback()
    {
        var host = MakeUser();  // user IS the host
        using var state = new TestGameState(host, NullLogger.Instance);
        var registration = MakeUserRegistration(host, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var tick = new StubTickService();

        var page = MakePage("abc-def", new StubUserService(host), session, nav, tick);
        int tickActionCalled = 0;
        page.TickFactory = () => (() => tickActionCalled++, 3);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(1, tick.RegisterCount);
        Assert.AreEqual(3, tick.LastInterval);
        tick.LastCallback?.Invoke();
        Assert.AreEqual(1, tickActionCalled);
    }

    [TestMethod]
    public async Task OnInitializedAsync_AsNonHost_DoesNotRegisterTick()
    {
        var host = MakeUser("Host");
        var player = MakeUser("Player");
        using var state = new TestGameState(host, NullLogger.Instance);
        var registration = new UserRegistration(player, Mock.Of<IDisposable>(), MakeLobby(state));

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var tick = new StubTickService();

        var page = MakePage("abc-def", new StubUserService(player), session, nav, tick);
        page.TickFactory = () => (() => { }, 1);

        await page.InvokeOnInitializedAsync();

        Assert.AreEqual(0, tick.RegisterCount);
    }

    // ── Kick detection via OnAfterRender ─────────────────────────────────────

    [TestMethod]
    public async Task OnAfterRender_KickedPlayer_LeavesSession()
    {
        var host = MakeUser("Host");
        var player = MakeUser("Player");
        using var state = new TestGameState(host, NullLogger.Instance);
        state.Execute(() => state.SetJoinable(true));
        state.RegisterPlayer(player);
        state.KickPlayer(player);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(new UserRegistration(player, Mock.Of<IDisposable>(), MakeLobby(state)));
        var page = MakePage("abc-def", new StubUserService(player), session, nav);

        await page.InvokeOnInitializedAsync();  // _initialized becomes true
        page.InvokeOnAfterRender(firstRender: true);

        Assert.AreEqual(1, session.LeaveCount);
        Assert.IsTrue(session.LastLeaveNavigateHome);
    }

    [TestMethod]
    public async Task OnAfterRender_NotKicked_NoLeave()
    {
        var host = MakeUser("Host");
        var player = MakeUser("Player");
        using var state = new TestGameState(host, NullLogger.Instance);
        state.Execute(() => state.SetJoinable(true));
        state.RegisterPlayer(player);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(new UserRegistration(player, Mock.Of<IDisposable>(), MakeLobby(state)));
        var page = MakePage("abc-def", new StubUserService(player), session, nav);

        await page.InvokeOnInitializedAsync();
        page.InvokeOnAfterRender(firstRender: true);

        Assert.AreEqual(0, session.LeaveCount);
    }

    [TestMethod]
    public async Task OnAfterRender_KickedButUninitialized_DoesNotLeave()
    {
        // Guard: if OnInitializedAsync redirected before _initialized was set,
        // the kick check must not fire (GameState is still default).
        var user = MakeUser();
        var nav = new StubNavigationService();
        var session = new StubGameSessionService(null);  // no session → redirect
        var page = MakePage("abc-def", new StubUserService(user), session, nav);

        await page.InvokeOnInitializedAsync();
        page.InvokeOnAfterRender(firstRender: true);

        Assert.AreEqual(0, session.LeaveCount);
    }

    // ── Dispose wiring ───────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dispose_DisposesTickAndStateSubscriptions_AndCallsLobbyDisposing()
    {
        var host = MakeUser();
        using var state = new TestGameState(host, NullLogger.Instance);
        var registration = MakeUserRegistration(host, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var tick = new StubTickService();

        var page = MakePage("abc-def", new StubUserService(host), session, nav, tick);
        page.TickFactory = () => (() => { }, 1);

        await page.InvokeOnInitializedAsync();

        var tickSubscription = tick.LastSubscription;
        Assert.IsNotNull(tickSubscription);
        Assert.IsFalse(tickSubscription.Disposed);

        page.Dispose();

        Assert.IsTrue(page.LobbyDisposingCalled, "OnLobbyDisposing must run during Dispose.");
        Assert.IsTrue(tickSubscription.Disposed, "Tick subscription must be disposed.");
    }

    // ── Wake lock acquire/release ────────────────────────────────────────────

    [TestMethod]
    public async Task OnAfterRenderAsync_AfterInit_AcquiresWakeLockOnce()
    {
        var user = MakeUser();
        using var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var wakeLock = new StubWakeLockService();
        var page = MakePage("abc-def", new StubUserService(user), session, nav, wakeLockService: wakeLock);

        await page.InvokeOnInitializedAsync();
        await page.InvokeOnAfterRenderAsync(firstRender: true);
        await page.InvokeOnAfterRenderAsync(firstRender: false);

        Assert.AreEqual(1, wakeLock.AcquireCount, "Acquire should be idempotent across multiple renders.");
    }

    [TestMethod]
    public async Task OnAfterRenderAsync_FirstRenderBeforeInitCompletes_StillAcquiresOnLaterRender()
    {
        // Regression guard: when OnInitializedAsync awaits past the first render,
        // _initialized is false at firstRender=true. The wake lock must still be
        // acquired on the next render rather than being skipped forever.
        var user = MakeUser();
        using var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var wakeLock = new StubWakeLockService();
        var page = MakePage("abc-def", new StubUserService(user), session, nav, wakeLockService: wakeLock);

        // Simulate Blazor's "first render fires while async init is still pending":
        // render arrives before _initialized = true.
        await page.InvokeOnAfterRenderAsync(firstRender: true);
        Assert.AreEqual(0, wakeLock.AcquireCount, "Should not acquire before init completes.");

        await page.InvokeOnInitializedAsync();
        await page.InvokeOnAfterRenderAsync(firstRender: false);

        Assert.AreEqual(1, wakeLock.AcquireCount, "Should acquire on the next render after init completes.");
    }

    [TestMethod]
    public async Task OnAfterRenderAsync_KickedPlayer_DoesNotAcquireWakeLock()
    {
        var host = MakeUser("Host");
        var player = MakeUser("Player");
        using var state = new TestGameState(host, NullLogger.Instance);
        state.Execute(() => state.SetJoinable(true));
        state.RegisterPlayer(player);
        state.KickPlayer(player);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(new UserRegistration(player, Mock.Of<IDisposable>(), MakeLobby(state)));
        var wakeLock = new StubWakeLockService();
        var page = MakePage("abc-def", new StubUserService(player), session, nav, wakeLockService: wakeLock);

        await page.InvokeOnInitializedAsync();
        page.InvokeOnAfterRender(firstRender: true);  // sets _kickHandled = true
        await page.InvokeOnAfterRenderAsync(firstRender: true);

        Assert.AreEqual(0, wakeLock.AcquireCount, "Kicked player should not acquire a wake lock before navigation.");
    }

    [TestMethod]
    public async Task OnAfterRenderAsync_NotInitialized_DoesNotAcquire()
    {
        var user = MakeUser();
        var nav = new StubNavigationService();
        var session = new StubGameSessionService(null);
        var wakeLock = new StubWakeLockService();
        var page = MakePage("abc-def", new StubUserService(user), session, nav, wakeLockService: wakeLock);

        await page.InvokeOnAfterRenderAsync(firstRender: true);

        Assert.AreEqual(0, wakeLock.AcquireCount);
    }

    [TestMethod]
    public async Task Dispose_ReleasesWakeLock()
    {
        var user = MakeUser();
        using var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var wakeLock = new StubWakeLockService();
        var page = MakePage("abc-def", new StubUserService(user), session, nav, wakeLockService: wakeLock);

        await page.InvokeOnInitializedAsync();
        await page.InvokeOnAfterRenderAsync(firstRender: true);

        page.Dispose();

        Assert.AreEqual(1, wakeLock.ReleaseCount, "Dispose must release the wake lock.");
    }

    [TestMethod]
    public async Task OnAfterRenderAsync_AcquireFails_RetriesOnNextRender()
    {
        // A transient JSDisconnect during initial circuit warm-up causes
        // AcquireAsync to return false. The page must un-stick its
        // _wakeLockAcquired guard so the next render tries again.
        var user = MakeUser();
        using var state = new TestGameState(user, NullLogger.Instance);
        var registration = MakeUserRegistration(user, state);

        var nav = new StubNavigationService();
        var session = new StubGameSessionService(registration);
        var wakeLock = new StubWakeLockService();
        wakeLock.AcquireResults.Enqueue(false);  // first attempt fails
        wakeLock.AcquireResults.Enqueue(true);   // second attempt succeeds
        var page = MakePage("abc-def", new StubUserService(user), session, nav, wakeLockService: wakeLock);

        await page.InvokeOnInitializedAsync();
        await page.InvokeOnAfterRenderAsync(firstRender: true);
        await page.InvokeOnAfterRenderAsync(firstRender: false);
        await page.InvokeOnAfterRenderAsync(firstRender: false);

        Assert.AreEqual(2, wakeLock.AcquireCount, "Failed acquire must retry, successful acquire must not.");
    }
}
