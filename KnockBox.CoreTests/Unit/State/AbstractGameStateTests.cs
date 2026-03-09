using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tests.Unit.State;

[TestClass]
public sealed class AbstractGameStateTests
{
    private sealed class TestGameState : AbstractGameState
    {
        public TestGameState(User host, ILogger logger) : base(host, logger) { }
    }

    private static User MakeUser(string name = "TestUser") =>
        new User(name, Guid.NewGuid().ToString());

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

    // ── IsJoinable / UpdateJoinableStatus ────────────────────────────────────

    [TestMethod]
    public void IsJoinable_InitiallyFalse()
    {
        using var state = MakeState();

        Assert.IsFalse(state.IsJoinable);
    }

    [TestMethod]
    public void UpdateJoinableStatus_ToTrue_SetsIsJoinable()
    {
        using var state = MakeState();

        state.UpdateJoinableStatus(true);

        Assert.IsTrue(state.IsJoinable);
    }

    [TestMethod]
    public void UpdateJoinableStatus_ToFalse_ClearsIsJoinable()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);

        state.UpdateJoinableStatus(false);

        Assert.IsFalse(state.IsJoinable);
    }

    [TestMethod]
    public async Task UpdateJoinableStatus_Changed_FiresStateChanged()
    {
        using var state = MakeState();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChangedEventManager.Subscribe(() =>
        {
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        });

        state.UpdateJoinableStatus(true);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreSame(tcs.Task, completed, "StateChanged was not fired.");
    }

    [TestMethod]
    public async Task UpdateJoinableStatus_SameValue_DoesNotFireStateChanged()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(false);

        var changeCount = 0;
        state.StateChangedEventManager.Subscribe(() =>
        {
            Interlocked.Increment(ref changeCount);
            return ValueTask.CompletedTask;
        });

        state.UpdateJoinableStatus(false);
        await Task.Delay(100);

        Assert.AreEqual(0, changeCount);
    }

    // ── Players ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Players_InitiallyEmpty()
    {
        using var state = MakeState();

        Assert.AreEqual(0, state.Players.Count);
    }

    // ── RegisterPlayer ───────────────────────────────────────────────────────

    [TestMethod]
    public void RegisterPlayer_WhenJoinable_AddsPlayer()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser("Player1");

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out _));
        Assert.AreEqual(1, state.Players.Count);
        Assert.IsTrue(state.Players.Contains(player));
    }

    [TestMethod]
    public void RegisterPlayer_WhenNotJoinable_ReturnsFailure()
    {
        using var state = MakeState();
        var player = MakeUser();

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(0, state.Players.Count);
    }

    [TestMethod]
    public void RegisterPlayer_Host_ReturnsFailure()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.UpdateJoinableStatus(true);

        var result = state.RegisterPlayer(host);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(0, state.Players.Count);
    }

    [TestMethod]
    public void RegisterPlayer_AlreadyRegistered_ReturnsFailure()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser();

        state.RegisterPlayer(player);
        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(1, state.Players.Count);
    }

    [TestMethod]
    public void RegisterPlayer_AfterUnregister_CanRejoin()
    {
        // Simulates the grace-period rejoin flow: player navigates to the home page
        // (old unsubscriber disposed), then re-enters the lobby code on the home page.
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser();

        var firstReg = state.RegisterPlayer(player);
        Assert.IsTrue(firstReg.TryGetSuccess(out var firstToken));

        // Player leaves the game page (LeaveCurrentSession disposes the token).
        firstToken.Dispose();
        Assert.AreEqual(0, state.Players.Count, "Player should be removed after unregistering.");

        // Player rejoins from the home page.
        var secondReg = state.RegisterPlayer(player);
        Assert.IsTrue(secondReg.IsSuccess, "Player should be able to rejoin after unregistering.");
        Assert.AreEqual(1, state.Players.Count, "Player should be back in the lobby after rejoining.");
    }

    [TestMethod]
    public void RegisterPlayer_Dispose_RemovesPlayer()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser();

        var reg = state.RegisterPlayer(player);
        Assert.IsTrue(reg.TryGetSuccess(out var unsubscriber));

        unsubscriber.Dispose();

        Assert.AreEqual(0, state.Players.Count);
    }

    [TestMethod]
    public void RegisterPlayer_Dispose_FiresPlayerUnregisteredEvent()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser();
        User? unregisteredPlayer = null;
        state.PlayerUnregistered += u => unregisteredPlayer = u;

        var reg = state.RegisterPlayer(player);
        reg.TryGetSuccess(out var unsubscriber);
        unsubscriber!.Dispose();

        Assert.AreSame(player, unregisteredPlayer);
    }

    [TestMethod]
    public void RegisterPlayer_AfterDispose_ReturnsFailure()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
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
        state.UpdateJoinableStatus(true);
        var player = MakeUser();
        state.RegisterPlayer(player);

        var result = state.KickPlayer(player);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(state.Players.Contains(player));
        Assert.IsTrue(state.KickedPlayers.Contains(player));
    }

    [TestMethod]
    public void KickPlayer_UnregisteredPlayer_ReturnsFailure()
    {
        using var state = MakeState();
        var player = MakeUser();

        var result = state.KickPlayer(player);

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void KickPlayer_KickedPlayer_CannotRejoin()
    {
        using var state = MakeState();
        state.UpdateJoinableStatus(true);
        var player = MakeUser();
        state.RegisterPlayer(player);
        state.KickPlayer(player);

        var result = state.RegisterPlayer(player);

        Assert.IsTrue(result.IsFailure);
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
        state.OnStateDisposed += () => fired = true;

        state.Dispose();

        Assert.IsTrue(fired);
    }

    [TestMethod]
    public void CreatedAt_IsApproximatelyNow()
    {
        var before = DateTime.UtcNow;
        using var state = MakeState();
        var after = DateTime.UtcNow;

        Assert.IsTrue(state.CreatedAt >= before);
        Assert.IsTrue(state.CreatedAt <= after);
    }
}
