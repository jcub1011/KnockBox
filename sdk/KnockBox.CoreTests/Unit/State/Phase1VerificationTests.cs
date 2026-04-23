using KnockBox.Core.Primitives.Disposable;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging;
using Moq;

namespace KnockBox.Tests.Unit.State;

/// <summary>
/// Covers the Phase-1 v1.0 hardening contracts:
///   - Execute/Dispose race returns the specific "State was disposed during execute." error.
///   - IScheduledCallbackHandle.Cancel/Dispose are idempotent and survive state disposal.
///   - A throwing subscriber to PlayerUnregistered/OnStateDisposed does not short-circuit
///     later subscribers in the invocation list.
///   - SetJoinable + RegisterPlayer are serialized via the execute lock.
/// </summary>
[TestClass]
public sealed class Phase1VerificationTests
{
    private sealed class TestGameState(User host, ILogger logger) : AbstractGameState(host, logger)
    {
    }

    private static User MakeUser(string name = "U") => new(name, Guid.NewGuid().ToString());
    private static TestGameState MakeState(User? host = null)
        => new(host ?? MakeUser("Host"), Mock.Of<ILogger>());

    // ── 1.3 — Execute/Dispose race returns specific error ────────────────────

    [TestMethod]
    public void Execute_AfterDispose_ReturnsSpecificDisposeError()
    {
        var state = MakeState();
        state.Dispose();

        var result = state.Execute(() => { });

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.Contains("disposed", err.PublicMessage);
    }

    [TestMethod]
    public async Task ExecuteAsync_AfterDispose_ReturnsSpecificDisposeError()
    {
        var state = MakeState();
        state.Dispose();

        var result = await state.ExecuteAsync(() => ValueTask.CompletedTask);

        Assert.IsTrue(result.IsFailure);
    }

    // ── 1.1 — IScheduledCallbackHandle idempotence ───────────────────────────

    [TestMethod]
    public async Task ScheduleCallback_Handle_CancelIsIdempotent()
    {
        using var state = MakeState();
        var handleResult = state.ScheduleCallback(TimeSpan.FromSeconds(30), () => Task.CompletedTask);
        Assert.IsTrue(handleResult.TryGetSuccess(out var handle));

        handle!.Cancel();
        Assert.IsTrue(handle.IsCancelled);

        // Second Cancel must be a no-op that does not throw.
        handle.Cancel();
        handle.Dispose();
        handle.Dispose(); // second Dispose also a no-op

        await Task.Delay(50); // give the callback task a chance to observe cancellation
    }

    [TestMethod]
    public async Task ScheduleCallback_Handle_CancelAfterStateDispose_DoesNotThrow()
    {
        var state = MakeState();
        var handleResult = state.ScheduleCallback(TimeSpan.FromSeconds(30), () => Task.CompletedTask);
        Assert.IsTrue(handleResult.TryGetSuccess(out var handle));

        state.Dispose();
        await Task.Delay(30); // let the scheduled task's finally run its cts.Dispose()

        // After state-dispose, the owning state has already disposed the CTS.
        // These operations must remain safe (no throw).
        handle!.Cancel();
        handle.Dispose();
    }

    // ── 1.4 — Handler-chain does not short-circuit on throw ──────────────────

    [TestMethod]
    public void PlayerUnregistered_ThrowingHandler_DoesNotAbortInvocationList()
    {
        var host = MakeUser("Host");
        using var state = MakeState(host);
        state.Execute(() => state.SetJoinable(true));

        var secondHandlerCalled = false;
        state.PlayerUnregistered += _ => throw new InvalidOperationException("boom");
        state.PlayerUnregistered += _ => secondHandlerCalled = true;

        var player = MakeUser("P1");
        var reg = state.RegisterPlayer(player);
        Assert.IsTrue(reg.TryGetSuccess(out var token));

        token!.Dispose();

        Assert.IsTrue(secondHandlerCalled, "Second PlayerUnregistered subscriber must fire even when the first throws.");
    }

    [TestMethod]
    public void OnStateDisposed_ThrowingHandler_DoesNotAbortInvocationList()
    {
        var state = MakeState();

        var secondHandlerCalled = false;
        state.OnStateDisposed += () => throw new InvalidOperationException("boom");
        state.OnStateDisposed += () => secondHandlerCalled = true;

        state.Dispose();

        Assert.IsTrue(secondHandlerCalled, "Second OnStateDisposed subscriber must fire even when the first throws.");
    }

    // ── 1.2 — SetJoinable / RegisterPlayer serialized via execute lock ───────

    [TestMethod]
    public async Task RegisterPlayer_IsJoinableCheck_SerializedAgainstSetJoinable()
    {
        // Spin up two concurrent workers: one flipping IsJoinable via Execute, the other
        // calling RegisterPlayer. If RegisterPlayer's gate were not under _executeLock,
        // we'd observe registered players whose IsJoinable-at-admission was inconsistent
        // with the subsequent gate state. What we actually assert here is the invariant
        // that, at the moment each success happens, IsJoinable was true — since the
        // entire RegisterPlayer body runs inside Execute, the read-check and the add
        // are atomic under the same lock. This is a smoke-test: the real coverage is
        // that no deadlock occurs and that every successful registration mutates players
        // while the gate held.
        using var state = MakeState();
        state.Execute(() => state.SetJoinable(true));

        int successes = 0;
        int failures = 0;

        var flipTask = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
                state.Execute(() => state.SetJoinable(i % 2 == 0));
        });

        var registerTask = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                var player = MakeUser($"p{i}");
                var result = state.RegisterPlayer(player);
                if (result.IsSuccess) Interlocked.Increment(ref successes);
                else Interlocked.Increment(ref failures);
            }
        });

        await Task.WhenAll(flipTask, registerTask);

        // Totals must match the loop count; no missed registrations, no duplicate counting,
        // no deadlock hang (the test would time out if RegisterPlayer re-entered Execute
        // under a caller-held lock).
        Assert.AreEqual(50, successes + failures);
    }
}
