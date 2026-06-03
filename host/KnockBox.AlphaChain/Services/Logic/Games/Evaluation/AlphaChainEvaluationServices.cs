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
        private readonly IBanLetterService _ban;
        private readonly IShotClockService _clock;
        private readonly IEngineEffects _effects;

        public AlphaChainEvaluationServices(AlphaChainGameState state, IRandomNumberService rng)
        {
            _ban = new BanLetterService(state, rng);
            _clock = new ShotClockService(state, this);
            _effects = new EngineEffects(state, this);
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

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBanLetterService)) return _ban;
            if (serviceType == typeof(IShotClockService)) return _clock;
            if (serviceType == typeof(IEngineEffects)) return _effects;
            return null;
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
        public string? RoundLeaderUserId => state.RoundLeaderUserId;

        public void AddNotice(EngineEffectEvent notice) => owner.Notices.Add(notice);

        public void RecordEraTaxSiphon(string collectorDisplayName, int amount)
            => owner.RecordEraTaxSiphon(collectorDisplayName, amount);

        public IEnumerable<AlphaChainPlayerState> OrderedActivePlayers()
            => state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderBy(p => TurnIndex(p.UserId));

        public AlphaChainPlayerState? PeekNextActivePlayer(string fromUserId)
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

            if (TryDeflect(victim) is { } mirror)
            {
                caster.QueuedTimePenaltySeconds += seconds;
                AddNotice(Reflect(mirror, victim, caster,
                    $"Reflected {source.GetName()} — −{seconds}s off {caster.DisplayName}'s next clock"));
                return;
            }

            victim.QueuedTimePenaltySeconds += seconds;
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

            if (TryDeflect(victim) is { } mirror)
            {
                caster.PersonalBannedLetter ??= letter;
                AddNotice(Reflect(mirror, victim, caster,
                    $"Reflected {source.GetName()} — '{char.ToUpperInvariant(letter)}' banned for {caster.DisplayName}"));
                return;
            }

            if (victim.PersonalBannedLetter is not null) return;

            victim.PersonalBannedLetter = letter;
            AddNotice(Attack(source, caster, victim, $"next word bans '{char.ToUpperInvariant(letter)}'"));
        }

        /// <summary>If the victim holds a Titanium Mirror, lets it block (decaying its shield) and returns
        /// the intercepting card so the caller can reflect the hit; else null.</summary>
        private static IModifierCard? TryDeflect(AlphaChainPlayerState victim)
        {
            var interceptor = ((IReadOnlyList<IModifierCard>)victim.EngineBay).AttackInterceptor();
            if (interceptor is IModifierCard card && interceptor.TryIntercept(victim, card))
                return card;
            return null;
        }

        private static EngineEffectEvent Attack(IModifierCard card, AlphaChainPlayerState holder, AlphaChainPlayerState target, string reason)
            => new(card.GetId().ToString(), card.GetName(), card.GetIcon(), EngineEffectClass.Offensive,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason);

        private static EngineEffectEvent Reflect(IModifierCard mirror, AlphaChainPlayerState holder, AlphaChainPlayerState target, string reason)
            => new(mirror.GetId().ToString(), mirror.GetName(), mirror.GetIcon(), EngineEffectClass.Special,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason, Negated: true);

        private int TurnIndex(string userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }
    }
}
