using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Services.Logic.RandomGeneration;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    /// <summary>
    /// The hand-rolled, plugin-internal <see cref="IServiceProvider"/> that backs
    /// <see cref="EngineEvaluationContext.Services"/>. One instance lives per room (built alongside
    /// the game context); it resolves the per-room engine services and carries the per-resolution
    /// scratch state (the current timestamp, the effect-notice sink, the era-tax siphon report) that
    /// the engine refreshes before each command dispatch via <see cref="BeginResolution"/>.
    /// </summary>
    public sealed class AlphaChainEvaluationServices : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public AlphaChainEvaluationServices(AlphaChainGameState state, IRandomNumberService rng, IModifierCardFactory factory)
        {
            // Core infrastructure services every room needs, regardless of which cards are in play.
            _services[typeof(IBanLetterService)] = new BanLetterService(state, rng);
            _services[typeof(IShotClockService)] = new ShotClockService(state, this);
            _services[typeof(IEngineEffects)] = new EngineEffects(state, this);
            // A player fact no card owns yet (Scattershot, forward-looking) lives here as core state.
            _services[typeof(IDoubleLetterTracker)] = new DoubleLetterTracker();

            // Card-contributed state services: instantiate the union across the whole catalogue so a
            // service exists even when the card writes to an opponent who doesn't hold it. Duplicate
            // contracts (e.g. Roulette Wheel + Toll Booth both want ICardBanService) collapse to one.
            foreach (var desc in factory.AllCardRoomServices())
                _services.TryAdd(desc.Contract, desc.Create(state));
        }

        /// <summary>The timestamp of the command currently being resolved (used for clock refills).</summary>
        public DateTimeOffset Now { get; private set; }

        /// <summary>The effect notices accumulated during the current resolution.</summary>
        public List<EngineEffectEvent> Notices { get; } = new();

        /// <summary>Display names of opponents who collected an era-tax siphon this resolution.</summary>
        public List<string> EraTaxCollectors { get; } = new();

        /// <summary>The largest single era-tax siphon collected this resolution (for the replay line).</summary>
        public int EraTaxBounty { get; private set; }

        /// <summary>Resets the per-resolution scratch state and stamps the current command time.</summary>
        public void BeginResolution(DateTimeOffset now)
        {
            Now = now;
            Notices.Clear();
            EraTaxCollectors.Clear();
            EraTaxBounty = 0;
        }

        internal void RecordEraTaxSiphon(string collectorDisplayName, int amount)
        {
            EraTaxCollectors.Add(collectorDisplayName);
            if (amount > EraTaxBounty) EraTaxBounty = amount;
        }

        public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);

        /// <summary>Typed convenience over <see cref="GetService"/> for plugin-internal callers.</summary>
        public T? Get<T>() where T : class => GetService(typeof(T)) as T;

        /// <summary>Every registered service that owns scoped player state (for lifecycle dispatch).</summary>
        public IEnumerable<IRoomStateService> StateServices => _services.Values.OfType<IRoomStateService>();

        /// <summary>Fires the turn-start boundary across every state service for <paramref name="player"/>.</summary>
        public void FireTurnStarted(AlphaChainPlayerState player)
        {
            foreach (var service in StateServices)
                service.OnTurnStarted(player);
        }

        /// <summary>Fires the era-start boundary across every state service for <paramref name="player"/>.</summary>
        public void FireEraStarted(AlphaChainPlayerState player)
        {
            foreach (var service in StateServices)
                service.OnEraStarted(player);
        }

        /// <summary>Clears all scoped state (back-to-lobby / new match).</summary>
        public void Reset()
        {
            foreach (var service in StateServices)
                service.Reset();
        }
    }

    /// <summary>Draws legal personal banned letters, dodging the current era ban.</summary>
    internal sealed class BanLetterService(AlphaChainGameState state, IRandomNumberService rng) : IBanLetterService
    {
        public char RollPersonalBan()
        {
            string pool = BanLetterPool.For(state.Settings.BanMode);
            char letter = BanLetterPool.Draw(state.Settings.BanMode, rng);
            if (state.BannedLetter is { } era && letter == era && pool.Length > 1)
            {
                int idx = pool.IndexOf(letter);
                letter = pool[(idx + 1) % pool.Length];
            }
            return letter;
        }
    }

    /// <summary>Arms the shot clock by delegating to the state's capability-driven computation.</summary>
    internal sealed class ShotClockService(AlphaChainGameState state, AlphaChainEvaluationServices owner) : IShotClockService
    {
        public int ComputeArmedSeconds(AlphaChainPlayerState player) => state.ComputeArmedShotClockSeconds(player);

        public void RefillToFull(AlphaChainPlayerState player)
            => state.PhaseEndTime = owner.Now.AddSeconds(state.ComputeArmedShotClockSeconds(player));
    }

    /// <summary>
    /// Applies automated attacks (routing each through the victim's <see cref="IAttackInterceptor"/>
    /// for block-and-reflect), navigates the turn order, and collects effect notices. Ports the old
    /// <c>EngineEffectResolver.Fire*</c>/<c>TryDeflect</c> logic onto the capability idiom.
    /// </summary>
    internal sealed class EngineEffects(AlphaChainGameState state, AlphaChainEvaluationServices owner) : IEngineEffects
    {
        public Guid? RoundLeaderUserId => state.RoundLeaderUserId;

        public void AddNotice(EngineEffectEvent notice) => owner.Notices.Add(notice);

        public void RecordEraTaxSiphon(string collectorDisplayName, int amount)
            => owner.RecordEraTaxSiphon(collectorDisplayName, amount);

        public IEnumerable<AlphaChainPlayerState> OrderedActivePlayers()
            => state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderBy(p => TurnIndex(p.UserId));

        public AlphaChainPlayerState? PeekNextActivePlayer(Guid fromUserId)
        {
            var order = state.TurnManager.TurnOrder;
            int start = order.IndexOf(fromUserId);
            if (start < 0 || order.Count == 0) return null;

            for (int i = 1; i <= order.Count; i++)
            {
                var id = order[(start + i) % order.Count];
                if (id == fromUserId) break;
                if (state.GamePlayers.TryGetValue(id, out var ps) && !ps.IsEliminated && !ps.HasLeft)
                    return ps;
            }
            return null;
        }

        public void TimeShave(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, int seconds)
        {
            if (seconds <= 0) return;
            var penalties = owner.Get<ITimePenaltyService>();

            if (TryDeflect(victim) is { } mirror)
            {
                penalties?.Queue(caster, seconds);
                AddNotice(Reflect(mirror, victim, caster,
                    $"Reflected {source.GetName()} — −{seconds}s off {caster.DisplayName}'s next clock"));
                return;
            }

            penalties?.Queue(victim, seconds);
            AddNotice(Attack(source, caster, victim, $"−{seconds}s next shot clock"));
        }

        public void Drain(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, int points)
        {
            if (points <= 0) return;

            if (TryDeflect(victim) is { } mirror)
            {
                caster.Score = Math.Max(0, caster.Score - points);
                AddNotice(Reflect(mirror, victim, caster,
                    $"Reflected {source.GetName()} — −{points} from {caster.DisplayName}"));
                return;
            }

            victim.Score = Math.Max(0, victim.Score - points);
            AddNotice(Attack(source, caster, victim, $"−{points} points"));
        }

        public void LetterHijack(IModifierCard source, AlphaChainPlayerState caster, AlphaChainPlayerState victim, char letter)
        {
            letter = char.ToLowerInvariant(letter);
            var hijack = owner.Get<IHijackBanService>();

            if (TryDeflect(victim) is { } mirror)
            {
                hijack?.Curse(caster, letter);
                AddNotice(Reflect(mirror, victim, caster,
                    $"Reflected {source.GetName()} — '{char.ToUpperInvariant(letter)}' banned for {caster.DisplayName}"));
                return;
            }

            // Curse is a no-op (returns false) when the victim already carries a hijack ban — match the
            // old "leave as-is, post no notice" behavior by gating the notice on a successful curse.
            if (hijack is null || !hijack.Curse(victim, letter)) return;
            AddNotice(Attack(source, caster, victim, $"next word bans '{char.ToUpperInvariant(letter)}'"));
        }

        /// <summary>If the victim holds a Titanium Mirror, lets it block (decaying its shield via the
        /// shield service) and returns the intercepting card so the caller can reflect the hit; else null.</summary>
        private IModifierCard? TryDeflect(AlphaChainPlayerState victim)
        {
            var interceptor = ((IReadOnlyList<IModifierCard>)victim.EngineBay).AttackInterceptor();
            if (interceptor is IModifierCard card && interceptor.TryIntercept(victim, card, owner))
                return card;
            return null;
        }

        private static EngineEffectEvent Attack(IModifierCard card, AlphaChainPlayerState holder, AlphaChainPlayerState target, string reason)
            => new(card.GetId(), card.GetName(), EngineEffectClass.Offensive,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason);

        private static EngineEffectEvent Reflect(IModifierCard mirror, AlphaChainPlayerState holder, AlphaChainPlayerState target, string reason)
            => new(mirror.GetId(), mirror.GetName(), EngineEffectClass.Special,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason, Negated: true);

        private int TurnIndex(Guid userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }
    }
}
