using System.Collections.Immutable;
using KnockBox.Core.Services.State.Users;
using KnockBox.Spardle;
using KnockBox.Spardle.Components;
using KnockBox.Spardle.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.SpardleTests.Unit;

[TestClass]
public class SpardleOpponentsRankingTests
{
    // ───────────────────────────────────────────────────────────────────────
    // ComputeLetterProgress
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ComputeLetterProgress_NoGuesses_ReturnsZero()
    {
        var ps = new PlayerState();
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(0, correct);
        Assert.AreEqual(0, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_ZeroWordLength_ReturnsZero()
    {
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("hello", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 0);
        Assert.AreEqual(0, correct);
        Assert.AreEqual(0, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_AllCorrect_ReturnsAllCorrect()
    {
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("hello", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(5, correct);
        Assert.AreEqual(0, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_AllPresent_ReturnsAllWrongPos()
    {
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("hello", LetterStatus.Present, LetterStatus.Present, LetterStatus.Present, LetterStatus.Present, LetterStatus.Present))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(0, correct);
        Assert.AreEqual(5, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_Mixed_CountsBoth()
    {
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("hello", LetterStatus.Correct, LetterStatus.Present, LetterStatus.Absent, LetterStatus.Correct, LetterStatus.Present))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(2, correct);
        Assert.AreEqual(2, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_PositionUpgradesFromPresentToCorrect_CountsAsCorrectOnly()
    {
        // Position 0: Present in guess 1, Correct in guess 2.
        // Best status wins, so it counts toward CorrectPos and not WrongPos.
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("aaaaa", LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent),
                MakeGuess("bbbbb", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(1, correct);
        Assert.AreEqual(0, wrong);
    }

    [TestMethod]
    public void ComputeLetterProgress_SamePositionPresentTwice_NotDoubleCounted()
    {
        // Position 0 is Present in two guesses: counted once toward WrongPos.
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("aaaaa", LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent),
                MakeGuess("bbbbb", LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent))
        };
        var (correct, wrong) = SpardleOpponentsRanking.ComputeLetterProgress(ps, 5);
        Assert.AreEqual(0, correct);
        Assert.AreEqual(1, wrong);
    }

    // ───────────────────────────────────────────────────────────────────────
    // ComputeRanked — Sprinter mode
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ComputeRanked_Sprinter_SolvedPlayersOrderedBySolveTime()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");

        SetSolved(state, alice, state.RoundStartTime.Value.AddSeconds(45));  // slowest
        SetSolved(state, bob, state.RoundStartTime.Value.AddSeconds(20));    // fastest
        SetSolved(state, carol, state.RoundStartTime.Value.AddSeconds(30));  // middle

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        Assert.HasCount(3, ranked);
        Assert.AreEqual("Bob", ranked[0].DisplayName);
        Assert.AreEqual("Carol", ranked[1].DisplayName);
        Assert.AreEqual("Alice", ranked[2].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_SolvedAhead_OfUnsolvedRegardlessOfProgress()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");

        // Alice unsolved with strong progress (4 greens).
        state.CreatePlayerState(alice.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent));

        // Bob solved very late (no greens visible from this guess statuses, but solved).
        SetSolved(state, bob, state.RoundStartTime.Value.AddMinutes(5));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        Assert.AreEqual("Bob", ranked[0].DisplayName);
        Assert.AreEqual("Alice", ranked[1].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_SolversTiedOnTime_FallBackToName()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var charlie = AddPlayer(state, "Charlie");
        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");

        var sameTime = state.RoundStartTime.Value.AddSeconds(30);
        SetSolved(state, charlie, sameTime);
        SetSolved(state, alice, sameTime);
        SetSolved(state, bob, sameTime);

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        Assert.AreEqual("Alice", ranked[0].DisplayName);
        Assert.AreEqual("Bob", ranked[1].DisplayName);
        Assert.AreEqual("Charlie", ranked[2].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_UnsolvedSortedByCorrectThenPresentThenName()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = DateTime.UtcNow;

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");
        var dave = AddPlayer(state, "Dave");

        // Alice: 1 green, 0 yellow.
        state.CreatePlayerState(alice.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        // Bob: 0 green, 3 yellow.
        state.CreatePlayerState(bob.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Present, LetterStatus.Present, LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent));
        // Carol: 2 green, 1 yellow.
        state.CreatePlayerState(carol.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent));
        // Dave: 2 green, 0 yellow.
        state.CreatePlayerState(dave.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // Carol (2g, 1y) > Dave (2g, 0y) > Alice (1g) > Bob (0g, 3y).
        Assert.AreEqual("Carol", ranked[0].DisplayName);
        Assert.AreEqual("Dave", ranked[1].DisplayName);
        Assert.AreEqual("Alice", ranked[2].DisplayName);
        Assert.AreEqual("Bob", ranked[3].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_UnsolvedFullyTied_FallBackToName()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");

        var charlie = AddPlayer(state, "Charlie");
        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");

        // All three: 1 green, 1 yellow.
        foreach (var p in new[] { charlie, alice, bob })
        {
            state.CreatePlayerState(p.Id).Guesses = ImmutableList.Create(
                MakeGuess("apple", LetterStatus.Correct, LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        }

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        Assert.AreEqual("Alice", ranked[0].DisplayName);
        Assert.AreEqual("Bob", ranked[1].DisplayName);
        Assert.AreEqual("Charlie", ranked[2].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_DnfRanksBelowStillGuessing_RegardlessOfProgress()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = DateTime.UtcNow;

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");

        // Alice DNF with 3 greens (good progress, but forfeited).
        var alicePs = state.CreatePlayerState(alice.Id);
        alicePs.HasFinishedRound = true;
        alicePs.Dnf = true;
        alicePs.FinishedAt = state.RoundStartTime.Value.AddSeconds(20);
        alicePs.Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent));

        // Bob still guessing with only 1 green.
        state.CreatePlayerState(bob.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // Bob still has a chance; Alice has forfeited — Alice must drop below regardless of greens.
        Assert.AreEqual("Bob", ranked[0].DisplayName);
        Assert.AreEqual("Alice", ranked[1].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_GiveUpDoesNotSurfaceAboveStillGuessing()
    {
        // Regression: a give-up player has FinishedAt set. The previous Sprinter sort used
        // FinishedAt as the universal tiebreak, which placed give-up forfeits at the top of
        // the unsolved group instead of dropping them to DNF.
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");

        // Alice gave up early — DNF with FinishedAt set.
        var alicePs = state.CreatePlayerState(alice.Id);
        alicePs.HasFinishedRound = true;
        alicePs.Dnf = true;
        alicePs.FinishedAt = state.RoundStartTime.Value.AddSeconds(10);

        // Bob and Carol still guessing.
        state.CreatePlayerState(bob.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        state.CreatePlayerState(carol.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // Carol (2g) > Bob (1g) > Alice (DNF, regardless of finish time).
        Assert.AreEqual("Carol", ranked[0].DisplayName);
        Assert.AreEqual("Bob", ranked[1].DisplayName);
        Assert.AreEqual("Alice", ranked[2].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Sprinter_MixedSolvedAndUnsolved_OrderingHolds()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");
        var dave = AddPlayer(state, "Dave");

        // Solved group (sorted by time): Bob 0:20, Alice 0:45.
        SetSolved(state, alice, state.RoundStartTime.Value.AddSeconds(45));
        SetSolved(state, bob, state.RoundStartTime.Value.AddSeconds(20));

        // Unsolved group (sorted by greens then yellows then name):
        //   Carol 2 green > Dave 0 green / 2 yellow.
        state.CreatePlayerState(carol.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        state.CreatePlayerState(dave.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Present, LetterStatus.Present, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        Assert.AreEqual("Bob", ranked[0].DisplayName);
        Assert.AreEqual("Alice", ranked[1].DisplayName);
        Assert.AreEqual("Carol", ranked[2].DisplayName);
        Assert.AreEqual("Dave", ranked[3].DisplayName);
    }

    // ───────────────────────────────────────────────────────────────────────
    // ComputeRanked — Tactician mode (legacy sort preserved)
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ComputeRanked_Tactician_KeepsLegacyClosestToFinishOrder()
    {
        var state = MakeState(WinConditionMode.Tactician, "apple");

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");

        // Alice solved.
        var alicePs = state.CreatePlayerState(alice.Id);
        alicePs.HasFinishedRound = true;
        alicePs.Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct));

        // Bob still guessing.
        state.CreatePlayerState(bob.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));

        // Carol DNF with no progress.
        state.CreatePlayerState(carol.Id).Dnf = true;

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // Tactician legacy: solved → still guessing → DNF.
        Assert.AreEqual("Alice", ranked[0].DisplayName);
        Assert.AreEqual("Bob", ranked[1].DisplayName);
        Assert.AreEqual("Carol", ranked[2].DisplayName);
    }

    [TestMethod]
    public void ComputeRanked_Tactician_DoesNotSortSolversByTime()
    {
        // In Tactician, two solvers should NOT be sorted by FinishedAt the way
        // Sprinter does — the round-results page applies the mode-aware tiebreak.
        // The real-time panel falls through to total-score then name on ties.
        var state = MakeState(WinConditionMode.Tactician, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");

        // Alice solved later, but with a higher TotalScore.
        var alicePs = state.CreatePlayerState(alice.Id);
        alicePs.HasFinishedRound = true;
        alicePs.FinishedAt = state.RoundStartTime.Value.AddSeconds(60);
        alicePs.TotalScore = 50;
        alicePs.Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct));

        // Bob solved earlier, lower TotalScore.
        var bobPs = state.CreatePlayerState(bob.Id);
        bobPs.HasFinishedRound = true;
        bobPs.FinishedAt = state.RoundStartTime.Value.AddSeconds(20);
        bobPs.TotalScore = 10;
        bobPs.Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Correct));

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // In Tactician, total score is the leader-surfacing tiebreak, so Alice ranks above Bob
        // even though Bob solved faster.
        Assert.AreEqual("Alice", ranked[0].DisplayName);
        Assert.AreEqual("Bob", ranked[1].DisplayName);
    }

    // ───────────────────────────────────────────────────────────────────────
    // FormatBadge
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FormatBadge_Sprinter_Solved_FormatsElapsedAsMinutesSeconds()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = state.RoundStartTime.Value.AddSeconds(125)  // 2:05
        };

        Assert.AreEqual("2:05", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Sprinter_Solved_SubMinute_PadsSeconds()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = state.RoundStartTime.Value.AddSeconds(7)  // 0:07
        };

        Assert.AreEqual("0:07", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Sprinter_Solved_ClampsNegativeToZero()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        // FinishedAt before RoundStartTime — defensive clamp to 0:00.
        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = state.RoundStartTime.Value.AddSeconds(-5)
        };

        Assert.AreEqual("0:00", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Sprinter_Solved_RoundStartMissing_FallsBackToCheck()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = null;

        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = DateTime.UtcNow
        };

        Assert.AreEqual("✓", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Sprinter_Solved_FinishedAtMissing_FallsBackToCheck()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = DateTime.UtcNow;

        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = null
        };

        Assert.AreEqual("✓", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Dnf_AlwaysShowsDnf()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        state.RoundStartTime = DateTime.UtcNow;

        var ps = new PlayerState { Dnf = true };
        Assert.AreEqual("DNF", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_StillGuessing_ShowsGuessCount()
    {
        var state = MakeState(WinConditionMode.Sprinter, "apple");
        var ps = new PlayerState
        {
            Guesses = ImmutableList.Create(
                MakeGuess("aaaaa", LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent),
                MakeGuess("bbbbb", LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent),
                MakeGuess("ccccc", LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent))
        };

        Assert.AreEqual("3", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    [TestMethod]
    public void FormatBadge_Tactician_Solved_StillCheckmark()
    {
        // Tactician should not display elapsed solve time on the live panel —
        // mode-aware placement happens in the round-results screen.
        var state = MakeState(WinConditionMode.Tactician, "apple");
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        var ps = new PlayerState
        {
            HasFinishedRound = true,
            FinishedAt = state.RoundStartTime.Value.AddSeconds(60)
        };

        Assert.AreEqual("✓", SpardleOpponentsRanking.FormatBadge(state, ps));
    }

    // ───────────────────────────────────────────────────────────────────────
    // Observer view: ComputeRanked composed with the razor's State.Players filter
    // ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ObserverViewRankAndFilter_ExcludesHost_OrdersByRoundState()
    {
        // Mirrors SpardleHostObserverPlayingView's two-step pipeline:
        //   1. SpardleOpponentsRanking.ComputeRanked(state)
        //   2. .Where(r => state.Players.Select(p => p.User.Id).Contains(r.User.Id))
        // With HostIsParticipant=true, the host IS in ComputeRanked's roster — the
        // razor's State.Players filter is what drops them from the observer panel.
        var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
        var state = new SpardleState(host, NullLogger.Instance);
        state.Execute(() => state.SetJoinable(true));
        state.WinCondition = WinConditionMode.Sprinter;
        state.TargetWord = "apple";
        state.RoundStartTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        // Host plays alongside the registered players — exercises the observer filter.
        state.SetHostIsParticipant(true);

        var alice = AddPlayer(state, "Alice");
        var bob = AddPlayer(state, "Bob");
        var carol = AddPlayer(state, "Carol");

        // Host: still-guessing (1 green). Filter should drop them.
        state.CreatePlayerState(host.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        // Alice: solved at +20s.
        SetSolved(state, alice, state.RoundStartTime.Value.AddSeconds(20));
        // Bob: still-guessing with 2 greens.
        state.CreatePlayerState(bob.Id).Guesses = ImmutableList.Create(
            MakeGuess("apple", LetterStatus.Correct, LetterStatus.Correct, LetterStatus.Absent, LetterStatus.Absent, LetterStatus.Absent));
        // Carol: DNF.
        var carolPs = state.CreatePlayerState(carol.Id);
        carolPs.HasFinishedRound = true;
        carolPs.Dnf = true;
        carolPs.FinishedAt = state.RoundStartTime.Value.AddSeconds(15);

        var ranked = SpardleOpponentsRanking.ComputeRanked(state);

        // Pre-filter: host is present.
        Assert.IsTrue(ranked.Any(r => r.User.Id == host.Id), "host should appear in raw ComputeRanked output when participating");

        // Apply the razor's filter.
        var visibleIds = state.Players.Select(p => p.User.Id).ToHashSet();
        var filtered = ranked.Where(r => visibleIds.Contains(r.User.Id)).ToList();

        Assert.IsFalse(filtered.Any(r => r.User.Id == host.Id), "host must be excluded by the observer filter");
        Assert.HasCount(3, filtered);
        Assert.AreEqual("Alice", filtered[0].DisplayName);
        Assert.AreEqual("Bob", filtered[1].DisplayName);
        Assert.AreEqual("Carol", filtered[2].DisplayName);
    }

    // ───────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────

    private static SpardleState MakeState(WinConditionMode mode, string targetWord)
    {
        var host = UserFactory.Create("Host", Guid.NewGuid().ToString());
        var state = new SpardleState(host, NullLogger.Instance);
        state.Execute(() => state.SetJoinable(true));
        state.WinCondition = mode;
        state.TargetWord = targetWord;
        // Treat host as a non-participating observer so the roster contains
        // only the registered players we add in each test.
        state.SetHostIsParticipant(false);
        return state;
    }

    private static User AddPlayer(SpardleState state, string name)
    {
        var user = UserFactory.Create(name, Guid.NewGuid().ToString());
        var reg = state.RegisterPlayer(user);
        Assert.IsTrue(reg.IsSuccess, $"RegisterPlayer({name}) failed");
        return user;
    }

    private static void SetSolved(SpardleState state, User player, DateTime finishedAt)
    {
        var ps = state.CreatePlayerState(player.Id);
        ps.HasFinishedRound = true;
        ps.FinishedAt = finishedAt;
    }

    private static GuessResult MakeGuess(string word, params LetterStatus[] statuses)
        => new()
        {
            Word = word,
            Statuses = statuses,
            IsCorrect = statuses.Length > 0 && statuses.All(s => s == LetterStatus.Correct)
        };
}
