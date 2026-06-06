using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.AlphaChain.Tests.Unit.Support;
using KnockBox.Core.Services.State.Users;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.AlphaChain.Tests.Unit.Logic.Games.Evaluation
{
    /// <summary>
    /// Unit tests for the room-scoped, player-keyed card-state services and the per-room container that
    /// instantiates them from the card catalogue. These exercise the migration target: card state now
    /// lives in services keyed by UserId, never on <see cref="AlphaChainPlayerState"/>.
    /// </summary>
    [TestClass]
    public class RoomStateServicesTests
    {
        private static AlphaChainEvaluationServices NewContainer()
        {
            var host = UserFactory.Create("Host", Guid.NewGuid());
            var state = new AlphaChainGameState(host, NullLogger<AlphaChainGameState>.Instance);
            return new AlphaChainEvaluationServices(state, new FixedRandomNumberService(), new ModifierCardFactory());
        }

        private static AlphaChainPlayerState Player(string id) => new() { UserId = Guid.NewGuid() };

        // ── Container wiring ────────────────────────────────────────────────────

        [TestMethod]
        public void Container_InstantiatesEveryCardServiceContract_OncePerContract()
        {
            var services = NewContainer();

            // Card-contributed state services exist room-wide (the catalogue union).
            Assert.IsNotNull(services.Get<IShieldService>());
            Assert.IsNotNull(services.Get<IPrismGuard>());
            Assert.IsNotNull(services.Get<ICardBanService>());
            Assert.IsNotNull(services.Get<ITimePenaltyService>());
            Assert.IsNotNull(services.Get<IHijackBanService>());
            // A core service no card owns yet (the double-letter fact) is still present.
            Assert.IsNotNull(services.Get<IDoubleLetterTracker>());

            // Roulette Wheel and Toll Booth both declare ICardBanService → collapsed to one instance.
            Assert.AreSame(services.Get<ICardBanService>(), services.Get<ICardBanService>());
        }

        [TestMethod]
        public void Container_Reset_ClearsEveryStateService()
        {
            var services = NewContainer();
            var p = Player("p1");

            services.Get<IShieldService>()!.GrantFresh(p);
            services.Get<IShieldService>()!.Decay(p, 0.4);
            services.Get<ITimePenaltyService>()!.Queue(p, 5);
            services.Get<IHijackBanService>()!.Curse(p, 'x');
            services.Get<ICardBanService>()!.Roll(p, ModifierId.TollBooth, 'q');
            services.Get<IDoubleLetterTracker>()!.Mark(p);

            services.Reset();

            Assert.AreEqual(1.0, services.Get<IShieldService>()!.GetMultiplier(p), "Shield resets to default 1.0.");
            Assert.AreEqual(0, services.Get<ITimePenaltyService>()!.Peek(p));
            Assert.IsNull(services.Get<IHijackBanService>()!.Peek(p));
            Assert.IsNull(services.Get<ICardBanService>()!.BanFor(p, ModifierId.TollBooth));
            Assert.IsFalse(services.Get<IDoubleLetterTracker>()!.HasPlayed(p));
        }

        // ── Shield ──────────────────────────────────────────────────────────────

        [TestMethod]
        public void Shield_DefaultsToOne_DecaysPerBlock_FloorsAtZero_AndIsPlayerKeyed()
        {
            var shield = NewContainer().Get<IShieldService>()!;
            var a = Player("a");
            var b = Player("b");

            Assert.AreEqual(1.0, shield.GetMultiplier(a), 1e-9, "Unseeded shield reads a passive 1.0.");

            shield.GrantFresh(a);
            shield.Decay(a, 0.1);
            Assert.AreEqual(0.9, shield.GetMultiplier(a), 1e-9, "Decays 0.1 per block.");

            for (int i = 0; i < 20; i++) shield.Decay(a, 0.1);
            Assert.AreEqual(0.0, shield.GetMultiplier(a), 1e-9, "Decay floors at 0.");

            Assert.AreEqual(1.0, shield.GetMultiplier(b), 1e-9, "Decaying player a leaves player b at 1.0.");

            shield.GrantFresh(a);
            Assert.AreEqual(1.0, shield.GetMultiplier(a), 1e-9, "GrantFresh resets a replacement mirror to 1.0.");
        }

        // ── Prism era guard ─────────────────────────────────────────────────────

        [TestMethod]
        public void PrismGuard_ConsumesOncePerEra_AndReArmsAtEraStart()
        {
            var guard = NewContainer().Get<IPrismGuard>()!;
            var p = Player("p");

            Assert.IsTrue(guard.TryConsume(p), "First consume this era succeeds.");
            Assert.IsFalse(guard.TryConsume(p), "Second consume this era is denied.");
            Assert.IsTrue(guard.HasConsumed(p));

            ((IRoomStateService)guard).OnTurnStarted(p);
            Assert.IsTrue(guard.HasConsumed(p), "A fresh turn does NOT re-arm the once-per-era guard.");

            ((IRoomStateService)guard).OnEraStarted(p);
            Assert.IsFalse(guard.HasConsumed(p), "A fresh era re-arms the guard.");
            Assert.IsTrue(guard.TryConsume(p), "And consume succeeds again.");
        }

        // ── Card bans ───────────────────────────────────────────────────────────

        [TestMethod]
        public void CardBans_AreKeyedPerPlayerAndCard_AndEraStartClearsOnlyThatPlayer()
        {
            var bans = NewContainer().Get<ICardBanService>()!;
            var a = Player("a");
            var b = Player("b");

            bans.Roll(a, ModifierId.TollBooth, 'q');
            bans.Roll(a, ModifierId.RouletteWheel, 'z');
            bans.Roll(b, ModifierId.TollBooth, 'x');

            Assert.AreEqual('q', bans.BanFor(a, ModifierId.TollBooth));
            Assert.AreEqual('z', bans.BanFor(a, ModifierId.RouletteWheel));
            CollectionAssert.AreEquivalent(new[] { 'q', 'z' }, bans.BansFor(a).ToArray());

            ((IRoomStateService)bans).OnEraStarted(a);
            Assert.IsNull(bans.BanFor(a, ModifierId.TollBooth), "Era start clears player a's bans.");
            Assert.AreEqual('x', bans.BanFor(b, ModifierId.TollBooth), "Player b's bans are untouched.");
        }

        // ── Time penalty (cross-player write) ─────────────────────────────────────

        [TestMethod]
        public void TimePenalty_QueuesOntoTheVictim_PeeksWithoutClearing_ConsumeClears()
        {
            var penalties = NewContainer().Get<ITimePenaltyService>()!;
            var victim = Player("victim");
            var other = Player("other");

            penalties.Queue(victim, 2);
            penalties.Queue(victim, 3);
            Assert.AreEqual(5, penalties.Peek(victim), "Shaves accumulate.");
            Assert.AreEqual(0, penalties.Peek(other), "Penalty is keyed to the victim only.");

            Assert.AreEqual(5, penalties.Peek(victim), "Peek does not clear.");
            Assert.AreEqual(5, penalties.ConsumeFor(victim), "Consume returns the queued total.");
            Assert.AreEqual(0, penalties.Peek(victim), "Consume clears it.");
        }

        // ── Hijack ban (cross-player write) ───────────────────────────────────────

        [TestMethod]
        public void HijackBan_CursesOnce_ConsumeClears_AndEraStartClears()
        {
            var hijack = NewContainer().Get<IHijackBanService>()!;
            var victim = Player("victim");

            Assert.IsTrue(hijack.Curse(victim, 'A'), "First curse applies (and lower-cases).");
            Assert.AreEqual('a', hijack.Peek(victim));
            Assert.IsFalse(hijack.Curse(victim, 'b'), "A second curse is a no-op while one is active.");
            Assert.AreEqual('a', hijack.Peek(victim), "The original ban is unchanged.");

            Assert.AreEqual('a', hijack.ConsumeFor(victim), "Consume returns and clears the ban.");
            Assert.IsNull(hijack.Peek(victim));

            hijack.Curse(victim, 'c');
            ((IRoomStateService)hijack).OnEraStarted(victim);
            Assert.IsNull(hijack.Peek(victim), "Era start clears a lingering hijack ban.");
        }
    }
}
