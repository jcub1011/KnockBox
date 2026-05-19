using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public sealed class AbstractGameStateTests
{
    private sealed class TestGameState(User host, ILogger logger) : AbstractGameState(host, logger)
    {
    }

    private static User MakeUser(string name = "TestUser") =>
        UserFactory.Create(name, Guid.NewGuid().ToString());

    private static ILogger MakeLogger() => Mock.Of<ILogger>();

    private static TestGameState MakeState(User? host = null)
    {
        host ??= MakeUser("Host");
        return new TestGameState(host, MakeLogger());
    }

    // ── Host ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Host_ReturnsProvidedHost()
    {
        var host = MakeUser("Alice");
        using var state = MakeState(host);

        Assert.AreSame(host, state.Host);
    }

    // ── IsJoinable / SetJoinable ─────────────────────────────────────────────

    [TestMethod]
    public void IsJoinable_InitiallyFalse()
    {
        using var state = MakeState();

        Assert.IsFalse(state.IsJoinable);
    }

    [TestMethod]
    public void SetJoinable_ToTrue_InsideExecute_SetsIsJoinable()
    {
        using var state = MakeState();

        state.Execute(() => state.SetJoinable(true));

        Assert.IsTrue(state.IsJoinable);
    }

    [TestMethod]
    public void SetJoinable_ToFalse_InsideExecute_ClearsIsJoinable()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));

        state.Execute(() => state.SetJoinable(false));

        Assert.IsFalse(state.IsJoinable);
    }

    [TestMethod]
    public void SetJoinable_OutsideExecute_Throws()
    {
        using var state = MakeState();

        Assert.Throws<InvalidOperationException>(() => state.SetJoinable(true));
    }

    [TestMethod]
    public void SetJoinable_FromInsideAnotherStatesExecute_Throws()
    {
        using var stateA = MakeState();
        using var stateB = MakeState();

        stateA.Execute(() =>
        {
            Assert.Throws<InvalidOperationException>(() => stateB.SetJoinable(true));
        });
    }

    [TestMethod]
    public async Task SetJoinable_InsideExecute_FiresStateChanged()
    {
        using var state = MakeState();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChangedEventManager.Subscribe(() =>
        {
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        state.Execute(() => state.SetJoinable(true));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(tcs.Task, completed, "StateChanged was not fired.");
    }

    [TestMethod]
    public void Execute_ActionThrows_DoesNotFireStateChanged()
    {
        using var state = MakeState();
        int notifyCount = 0;
        state.StateChangedEventManager.Subscribe(() =>
        {
            Interlocked.Increment(ref notifyCount);
            return ValueTask.CompletedTask;
        });

        var result = state.Execute(() => throw new InvalidOperationException("boom"));

        Assert.IsTrue(result.IsFailure, "Execute should fail when the action throws.");
        Assert.AreEqual(0, Volatile.Read(ref notifyCount), "StateChanged must not fire when the action threw.");
    }

    [TestMethod]
    public async Task ExecuteAsync_ActionThrows_DoesNotFireStateChanged()
    {
        using var state = MakeState();
        int notifyCount = 0;
        state.StateChangedEventManager.Subscribe(() =>
        {
            Interlocked.Increment(ref notifyCount);
            return ValueTask.CompletedTask;
        });

        var result = await state.ExecuteAsync(() => throw new InvalidOperationException("boom"));

        Assert.IsTrue(result.IsFailure, "ExecuteAsync should fail when the action throws.");
        Assert.AreEqual(0, Volatile.Read(ref notifyCount), "StateChanged must not fire when the action threw.");
    }

    // ── Players ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Players_InitiallyEmpty()
    {
        using var state = MakeState();

        Assert.IsEmpty(state.Players);
    }

    // ── RegisterPlayer ───────────────────────────────────────────────────────

    [TestMethod]
    public void RegisterPlayer_WhenJoinable_AddsPlayer()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser("Player1");

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out _));
        Assert.HasCount(1, state.Players);
        Assert.IsTrue(state.Players.Any(p => ReferenceEquals(p.User, player)));
    }

    [TestMethod]
    public void RegisterPlayer_WhenNotJoinable_ReturnsFailure()
    {
        using var state = MakeState();
        var player = MakeUser();

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
        Assert.IsEmpty(state.Players);
    }

    [TestMethod]
    public void RegisterPlayer_Host_ReturnsFailure()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var result = state.RegisterPlayer(host);

        Assert.IsTrue(result.IsFailure);
        Assert.IsEmpty(state.Players);
    }

    [TestMethod]
    public void RegisterPlayer_AlreadyRegistered_Succeeds_WithoutDuplicating()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();

        state.RegisterPlayer(player);
        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsSuccess, "Re-registering a player already in the lobby should succeed.");
        Assert.HasCount(1, state.Players, "Player should not be duplicated in the player list.");
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_OldTokenBecomesStale()
    {
        // Simulates: player still registered (grace period active), re-joins from home page.
        // The old token held by GameSessionState should become a no-op on dispose so the
        // player is not accidentally removed from the lobby by the eviction of the stale session.
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();

        var firstReg = state.RegisterPlayer(player);
        Assert.IsTrue(firstReg.TryGetSuccess(out var oldToken));

        // Player re-joins — this replaces the token in the dictionary.
        var secondReg = state.RegisterPlayer(player);
        Assert.IsTrue(secondReg.IsSuccess);

        // The old token (still held by the previous UserRegistration) is now stale.
        oldToken.Dispose();

        Assert.HasCount(1, state.Players, "Disposing the stale token should not remove the player.");
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_NewTokenRemovesPlayer()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();

        state.RegisterPlayer(player);
        var secondReg = state.RegisterPlayer(player);
        Assert.IsTrue(secondReg.TryGetSuccess(out var newToken));

        newToken.Dispose();

        Assert.IsEmpty(state.Players, "Disposing the current token should properly remove the player.");
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_PlayerUnregisteredNotFiredForStaleToken()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        int eventCount = 0;
        state.SubscribePlayerUnregistered(_ => eventCount++);

        var firstReg = state.RegisterPlayer(player);
        Assert.IsTrue(firstReg.TryGetSuccess(out var oldToken));
        state.RegisterPlayer(player); // supersedes oldToken

        oldToken.Dispose(); // stale — should not fire PlayerUnregistered

        Assert.AreEqual(0, eventCount, "PlayerUnregistered should not fire when a stale token is disposed.");
    }

    [TestMethod]
    public void RegisterPlayer_Rejoin_PlayerUnregisteredFiredForNewToken()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        User? unregisteredPlayer = null;
        state.SubscribePlayerUnregistered(u => unregisteredPlayer = u);

        state.RegisterPlayer(player); // oldToken — will be superseded
        var secondReg = state.RegisterPlayer(player);
        Assert.IsTrue(secondReg.TryGetSuccess(out var newToken));

        newToken.Dispose();

        Assert.AreSame(player, unregisteredPlayer, "PlayerUnregistered should fire when the current token is disposed.");
    }

    [TestMethod]
    public void RegisterPlayer_Dispose_RemovesPlayer()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();

        var reg = state.RegisterPlayer(player);
        Assert.IsTrue(reg.TryGetSuccess(out var unsubscriber));

        unsubscriber.Dispose();

        Assert.IsEmpty(state.Players);
    }

    [TestMethod]
    public void RegisterPlayer_Dispose_FiresPlayerUnregisteredEvent()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        User? unregisteredPlayer = null;
        state.SubscribePlayerUnregistered(u => unregisteredPlayer = u);

        var reg = state.RegisterPlayer(player);
        reg.TryGetSuccess(out var unsubscriber);
        unsubscriber!.Dispose();

        Assert.AreSame(player, unregisteredPlayer);
    }

    [TestMethod]
    public void RegisterPlayer_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        state.Dispose();
        var player = MakeUser();

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
    }

    // ── KickPlayer ───────────────────────────────────────────────────────────

    [TestMethod]
    public void KickPlayer_RegisteredPlayer_Succeeds()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);

        var result = state.KickPlayer(state.Host, player);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(state.Players.Any(p => ReferenceEquals(p.User, player)));
        Assert.IsTrue(state.IsKicked(player));
    }

    [TestMethod]
    public void KickPlayer_UnregisteredPlayer_ReturnsFailure()
    {
        using var state = MakeState();
        var player = MakeUser();

        var result = state.KickPlayer(state.Host, player);

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void KickPlayer_KickedPlayer_CannotRejoin()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);
        state.KickPlayer(state.Host, player);

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public async Task KickPlayer_FiresPlayerUnregisteredExactlyOnce()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int fired = 0;
        state.SubscribePlayerUnregistered(_ =>
        {
            if (Interlocked.Increment(ref fired) == 1)
                tcs.TrySetResult();
        });

        state.KickPlayer(state.Host, player);

        // Deterministically await the first notification.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.AreSame(tcs.Task, completed, "PlayerUnregistered should have fired within timeout.");

        Assert.AreEqual(1, fired, "PlayerUnregistered should fire exactly once per kick.");
    }

    [TestMethod]
    public async Task KickPlayer_FiresStateChangedExactlyOnce()
    {
        using var state = MakeState();

        var setupTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int setupNotifications = 0;
        using var setupSub = state.StateChangedEventManager.Subscribe(() =>
        {
            if (Interlocked.Increment(ref setupNotifications) == 2)
                setupTcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);

        // Deterministically drain the initial setup notifications (UpdateJoinableStatus + RegisterPlayer).
        await Task.WhenAny(setupTcs.Task, Task.Delay(1000));
        setupSub.Dispose();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int notifications = 0;
        using var subscription = state.StateChangedEventManager.Subscribe(() =>
        {
            if (Interlocked.Increment(ref notifications) == 1)
                tcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        state.KickPlayer(state.Host, player);

        // Deterministically await the first notification.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
        Assert.AreSame(tcs.Task, completed, "StateChanged should have fired within timeout.");

        Assert.AreEqual(1, notifications, "KickPlayer should produce exactly one StateChanged notification.");
    }

    [TestMethod]
    public void KickPlayer_RemovesFromPlayersAndMarksKicked()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);

        state.KickPlayer(state.Host, player);

        Assert.IsFalse(state.Players.Any(p => ReferenceEquals(p.User, player)));
        Assert.IsTrue(state.IsKicked(player),
            "Kicked player should appear in KickedPlayers after KickPlayer completes.");
    }

    [TestMethod]
    public void KickedPlayers_ContainsKickedUser()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);

        state.KickPlayer(state.Host, player);

        Assert.Contains(player, state.KickedPlayers);
    }

    [TestMethod]
    public void KickPlayer_NonHostCaller_ReturnsFailureAndDoesNotKick()
    {
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var attacker = MakeUser("Attacker");
        var victim = MakeUser("Victim");
        state.RegisterPlayer(attacker);
        state.RegisterPlayer(victim);

        var result = state.KickPlayer(attacker, victim);

        Assert.IsTrue(result.IsFailure, "Non-host caller should not be allowed to kick.");
        Assert.IsTrue(state.Players.Any(p => ReferenceEquals(p.User, victim)),
            "Victim must remain registered after a rejected kick.");
        Assert.IsFalse(state.IsKicked(victim), "Victim must not be marked kicked.");
    }

    // ── AllowRejoinAfterStart ────────────────────────────────────────────────

    private sealed class TestGameStateAllowingRejoin(User host, ILogger logger) : AbstractGameState(host, logger)
    {
        protected override bool AllowRejoinAfterStart => true;
    }

    [TestMethod]
    public void RegisterPlayer_AfterStart_WithRejoinAllowed_AdmitsPriorPlayer()
    {
        using var state = new TestGameStateAllowingRejoin(MakeUser("Host"), MakeLogger());
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();

        var firstReg = state.RegisterPlayer(player);
        Assert.IsTrue(firstReg.TryGetSuccess(out var firstUnsub));
        firstUnsub!.Dispose();

        state.Execute(() => state.SetJoinable(false));

        var rejoin = state.RegisterPlayer(player);

        Assert.IsTrue(rejoin.IsSuccess, "Prior lobby member should be allowed to rejoin after start.");
    }

    [TestMethod]
    public void RegisterPlayer_AfterStart_WithRejoinAllowed_RejectsStranger()
    {
        using var state = new TestGameStateAllowingRejoin(MakeUser("Host"), MakeLogger());
        // Never opened the lobby — stranger has no prior-join record.
        var stranger = MakeUser("Stranger");

        var result = state.RegisterPlayer(stranger);

        Assert.IsTrue(result.IsFailure,
            "AllowRejoinAfterStart should not let strangers in once IsJoinable is false.");
    }

    [TestMethod]
    public void RegisterPlayer_AfterStart_WithRejoinAllowed_RejectsKickedPlayer()
    {
        var host = MakeUser("Host");
        using var state = new TestGameStateAllowingRejoin(host, MakeLogger());
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        state.RegisterPlayer(player);
        state.KickPlayer(host, player);
        state.Execute(() => state.SetJoinable(false));

        var rejoin = state.RegisterPlayer(player);

        Assert.IsTrue(rejoin.IsFailure,
            "Kicked players must not bypass the kicked-set via AllowRejoinAfterStart.");
    }

    [TestMethod]
    public void RegisterPlayer_AfterStart_WithRejoinDisallowed_RejectsPriorPlayer()
    {
        // Default TestGameState leaves AllowRejoinAfterStart == false.
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));
        var player = MakeUser();
        var firstReg = state.RegisterPlayer(player);
        Assert.IsTrue(firstReg.TryGetSuccess(out var firstUnsub));
        firstUnsub!.Dispose();

        state.Execute(() => state.SetJoinable(false));

        var rejoin = state.RegisterPlayer(player);

        Assert.IsTrue(rejoin.IsFailure,
            "Without AllowRejoinAfterStart, prior players must not be re-admitted once the lobby closes.");
    }

    // ── Execute ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Execute_Action_RunsSuccessfully()
    {
        using var state = MakeState();
        var executed = false;

        var result = state.Execute(() => executed = true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public void Execute_Action_ExceptionInAction_ReturnsFailure()
    {
        using var state = MakeState();

        var result = state.Execute(() => throw new InvalidOperationException("boom"));

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void Execute_Action_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Dispose();

        var result = state.Execute(() => { });

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void Execute_ValueReturn_ReturnsValue()
    {
        using var state = MakeState();

        var result = state.Execute(() => 42);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out var val));
        Assert.AreEqual(42, val);
    }

    [TestMethod]
    public void Execute_ValueReturn_ExceptionInAction_ReturnsFailure()
    {
        using var state = MakeState();

        var result = state.Execute<int>(() => throw new InvalidOperationException("fail"));

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void Execute_ValueReturn_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Dispose();

        var result = state.Execute(() => 1);

        Assert.IsTrue(result.IsFailure);
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_RunsSuccessfully()
    {
        using var state = MakeState();
        var executed = false;

        var result = await state.ExecuteAsync(() =>
        {
            executed = true;
            return ValueTask.CompletedTask;
        });

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(executed);
    }

    [TestMethod]
    public async Task ExecuteAsync_ExceptionInAction_ReturnsFailure()
    {
        using var state = MakeState();

        var result = await state.ExecuteAsync(() =>
            throw new InvalidOperationException("async boom"));

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public async Task ExecuteAsync_Canceled_ReturnsCanceled()
    {
        using var state = MakeState();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await state.ExecuteAsync(() => ValueTask.CompletedTask, cts.Token);

        Assert.IsTrue(result.IsCanceled);
    }

    [TestMethod]
    public async Task ExecuteAsync_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Dispose();

        var result = await state.ExecuteAsync(() => ValueTask.CompletedTask);

        Assert.IsTrue(result.IsFailure);
    }

    // ── WithExclusiveRead ─────────────────────────────────────────────────────

    [TestMethod]
    public void WithExclusiveRead_RunsSuccessfully()
    {
        using var state = MakeState();
        var called = false;

        var result = state.WithExclusiveRead(() => called = true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(called);
    }

    [TestMethod]
    public void WithExclusiveRead_ExceptionInAction_ReturnsFailure()
    {
        using var state = MakeState();

        var result = state.WithExclusiveRead(() => throw new InvalidOperationException("read fail"));

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void WithExclusiveRead_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Dispose();

        var result = state.WithExclusiveRead(() => { });

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public async Task WithExclusiveReadAsync_RunsSuccessfully()
    {
        using var state = MakeState();
        var called = false;

        var result = await state.WithExclusiveReadAsync(() =>
        {
            called = true;
            return ValueTask.CompletedTask;
        });

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(called);
    }

    [TestMethod]
    public async Task WithExclusiveReadAsync_Canceled_ReturnsCanceled()
    {
        using var state = MakeState();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await state.WithExclusiveReadAsync(() => ValueTask.CompletedTask, cts.Token);

        Assert.IsTrue(result.IsCanceled);
    }

    // ── Execute fires StateChanged ────────────────────────────────────────────

    [TestMethod]
    public async Task Execute_AfterAction_FiresStateChangedEvent()
    {
        using var state = MakeState();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChangedEventManager.Subscribe(() =>
        {
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        state.Execute(() => { });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(tcs.Task, completed, "StateChanged was not fired after Execute.");
    }

    // ── ScheduleCallback ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ScheduleCallback_ExecutesAfterDelay()
    {
        using var state = MakeState();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        state.ScheduleCallback(TimeSpan.FromMilliseconds(50), () =>
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        });

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(tcs.Task, completed, "Scheduled callback was not executed.");
    }

    [TestMethod]
    public async Task ScheduleCallback_Canceled_DoesNotExecute()
    {
        using var state = MakeState();
        var executed = false;

        var result = state.ScheduleCallback(TimeSpan.FromSeconds(10), () =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.IsTrue(result.TryGetSuccess(out var cts));
        cts.Cancel();

        await Task.Delay(200);
        Assert.IsFalse(executed);
    }

    [TestMethod]
    public void ScheduleCallback_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.Dispose();

        var result = state.ScheduleCallback(TimeSpan.FromSeconds(1), () => Task.CompletedTask);

        Assert.IsTrue(result.IsFailure);
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_SetsIsDisposed()
    {
        var state = MakeState();

        state.Dispose();

        Assert.IsTrue(state.IsDisposed);
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var state = MakeState();

        state.Dispose();
        state.Dispose();

        Assert.IsTrue(state.IsDisposed);
    }

    [TestMethod]
    public void Dispose_FiresOnStateDisposedEvent()
    {
        var state = MakeState();
        var fired = false;
        state.SubscribeStateDisposed(() => fired = true);

        state.Dispose();

        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void CreatedAt_IsApproximatelyNow()
    {
        var before = DateTime.UtcNow;
        using var state = MakeState();
        var after = DateTime.UtcNow;

        Assert.IsGreaterThanOrEqualTo(before, state.CreatedAt);
        Assert.IsLessThanOrEqualTo(after, state.CreatedAt);
    }
}
