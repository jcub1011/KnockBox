using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Evaluation
{
    // ── Card-owned state services ───────────────────────────────────────────────
    //
    // Each service is the single, room-scoped home for one card's per-player state, keyed by UserId
    // in an internal dictionary (mirroring AlphaChainGameState.GamePlayers). A card reads/writes its
    // state through the service via EngineEvaluationContext.Service<T>(); the service owns its own
    // scope reset (IRoomStateService). The plugin never stores card state on AlphaChainPlayerState.

    /// <summary>The Titanium Mirror's live shield multiplier (starts 1.0, decays per block; persists
    /// across eras, reset to 1.0 only when a fresh mirror is dealt).</summary>
    public interface IShieldService
    {
        /// <summary>The owner's current shield multiplier (1.0 when none recorded).</summary>
        double GetMultiplier(AlphaChainPlayerState player);

        /// <summary>Decays the owner's shield by <paramref name="step"/>, floored at 0.</summary>
        void Decay(AlphaChainPlayerState player, double step);

        /// <summary>Resets the owner's shield to a fresh, un-decayed 1.0 (a replacement mirror was dealt).</summary>
        void GrantFresh(AlphaChainPlayerState player);
    }

    /// <summary>Hyper-Drive's era latch (short clock + doubled multipliers once it fires).</summary>
    public interface IHyperDriveService
    {
        /// <summary>Whether the owner's overdrive is latched this era.</summary>
        bool IsLatched(AlphaChainPlayerState player);

        /// <summary>Latches the owner's overdrive for the rest of the era.</summary>
        void Latch(AlphaChainPlayerState player);
    }

    /// <summary>The Prism's once-per-turn refill gate.</summary>
    public interface IPrismTurnGuard
    {
        /// <summary>Consumes the owner's per-turn refill: true exactly once per turn (then false until
        /// the next turn arms).</summary>
        bool TryConsume(AlphaChainPlayerState player);

        /// <summary>Whether the owner has already consumed their refill this turn (non-consuming read).</summary>
        bool HasConsumed(AlphaChainPlayerState player);
    }

    /// <summary>Era-rolled personal banned letters (Roulette Wheel, Toll Booth), keyed per card.</summary>
    public interface ICardBanService
    {
        /// <summary>Records the era-rolled ban <paramref name="letter"/> for <paramref name="card"/> on the owner.</summary>
        void Roll(AlphaChainPlayerState player, ModifierId card, char letter);

        /// <summary>The owner's ban rolled by <paramref name="card"/> this era, or null.</summary>
        char? BanFor(AlphaChainPlayerState player, ModifierId card);

        /// <summary>Every era-rolled card-ban currently in effect for the owner.</summary>
        IReadOnlyCollection<char> BansFor(AlphaChainPlayerState player);
    }

    /// <summary>Shot-clock seconds queued onto a player by out-of-turn time-shave attacks (Flak Cannon).</summary>
    public interface ITimePenaltyService
    {
        /// <summary>Adds <paramref name="seconds"/> to <paramref name="victim"/>'s queued shot-clock penalty.</summary>
        void Queue(AlphaChainPlayerState victim, int seconds);

        /// <summary>The seconds currently queued for <paramref name="player"/> (0 when none), without clearing.</summary>
        int Peek(AlphaChainPlayerState player);

        /// <summary>Returns and clears the seconds queued for <paramref name="player"/> (0 when none).</summary>
        int ConsumeFor(AlphaChainPlayerState player);
    }

    /// <summary>A transient personal banned letter forced onto a player by an opponent (Bait &amp; Switch).</summary>
    public interface IHijackBanService
    {
        /// <summary>Curses <paramref name="victim"/> with <paramref name="letter"/> for their next word.
        /// Returns false (no-op) when they already carry a hijack ban.</summary>
        bool Curse(AlphaChainPlayerState victim, char letter);

        /// <summary>The owner's active hijack ban, or null — without consuming it.</summary>
        char? Peek(AlphaChainPlayerState player);

        /// <summary>Returns and clears the owner's active hijack ban (consumed by their next submission).</summary>
        char? ConsumeFor(AlphaChainPlayerState player);
    }

    /// <summary>Tracks whether a player has played a double-letter word this era — the target test for
    /// an opponent's Scattershot (forward-looking; no reader yet).</summary>
    public interface IDoubleLetterTracker
    {
        /// <summary>Marks that the owner played a double-letter word this era.</summary>
        void Mark(AlphaChainPlayerState player);

        /// <summary>Whether the owner has played a double-letter word this era.</summary>
        bool HasPlayed(AlphaChainPlayerState player);
    }

    // ── Implementations ─────────────────────────────────────────────────────────

    internal sealed class ShieldService : IShieldService, IRoomStateService
    {
        private readonly Dictionary<string, double> _multiplier = new(StringComparer.Ordinal);

        public double GetMultiplier(AlphaChainPlayerState player) => _multiplier.GetValueOrDefault(player.UserId, 1.0);
        public void Decay(AlphaChainPlayerState player, double step)
            => _multiplier[player.UserId] = Math.Max(0.0, GetMultiplier(player) - step);
        public void GrantFresh(AlphaChainPlayerState player) => _multiplier[player.UserId] = 1.0;

        // The shield deliberately persists across eras (no OnEraStarted); GrantFresh on a fresh deal
        // is its only reset short of back-to-lobby.
        public void Reset() => _multiplier.Clear();
    }

    internal sealed class HyperDriveService : IHyperDriveService, IRoomStateService
    {
        private readonly HashSet<string> _latched = new(StringComparer.Ordinal);

        public bool IsLatched(AlphaChainPlayerState player) => _latched.Contains(player.UserId);
        public void Latch(AlphaChainPlayerState player) => _latched.Add(player.UserId);

        public void OnEraStarted(AlphaChainPlayerState player) => _latched.Remove(player.UserId);
        public void Reset() => _latched.Clear();
    }

    internal sealed class PrismTurnGuard : IPrismTurnGuard, IRoomStateService
    {
        private readonly HashSet<string> _usedThisTurn = new(StringComparer.Ordinal);

        public bool TryConsume(AlphaChainPlayerState player) => _usedThisTurn.Add(player.UserId);
        public bool HasConsumed(AlphaChainPlayerState player) => _usedThisTurn.Contains(player.UserId);

        public void OnTurnStarted(AlphaChainPlayerState player) => _usedThisTurn.Remove(player.UserId);
        public void Reset() => _usedThisTurn.Clear();
    }

    internal sealed class CardBanService : ICardBanService, IRoomStateService
    {
        private readonly Dictionary<string, Dictionary<ModifierId, char>> _bans = new(StringComparer.Ordinal);

        public void Roll(AlphaChainPlayerState player, ModifierId card, char letter)
        {
            if (!_bans.TryGetValue(player.UserId, out var byCard))
                _bans[player.UserId] = byCard = new Dictionary<ModifierId, char>();
            byCard[card] = letter;
        }

        public char? BanFor(AlphaChainPlayerState player, ModifierId card)
            => _bans.TryGetValue(player.UserId, out var byCard) && byCard.TryGetValue(card, out var c) ? c : null;

        public IReadOnlyCollection<char> BansFor(AlphaChainPlayerState player)
            => _bans.TryGetValue(player.UserId, out var byCard) ? byCard.Values : [];

        public void OnEraStarted(AlphaChainPlayerState player) => _bans.Remove(player.UserId);
        public void Reset() => _bans.Clear();
    }

    internal sealed class TimePenaltyService : ITimePenaltyService, IRoomStateService
    {
        private readonly Dictionary<string, int> _queued = new(StringComparer.Ordinal);

        public void Queue(AlphaChainPlayerState victim, int seconds)
        {
            if (seconds <= 0) return;
            _queued[victim.UserId] = _queued.GetValueOrDefault(victim.UserId) + seconds;
        }

        public int Peek(AlphaChainPlayerState player) => _queued.GetValueOrDefault(player.UserId);

        public int ConsumeFor(AlphaChainPlayerState player)
        {
            if (_queued.Remove(player.UserId, out var seconds)) return seconds;
            return 0;
        }

        public void Reset() => _queued.Clear();
    }

    internal sealed class HijackBanService : IHijackBanService, IRoomStateService
    {
        private readonly Dictionary<string, char> _ban = new(StringComparer.Ordinal);

        public bool Curse(AlphaChainPlayerState victim, char letter)
        {
            if (_ban.ContainsKey(victim.UserId)) return false;
            _ban[victim.UserId] = char.ToLowerInvariant(letter);
            return true;
        }

        public char? Peek(AlphaChainPlayerState player) => _ban.TryGetValue(player.UserId, out var c) ? c : null;

        public char? ConsumeFor(AlphaChainPlayerState player) => _ban.Remove(player.UserId, out var c) ? c : null;

        public void OnEraStarted(AlphaChainPlayerState player) => _ban.Remove(player.UserId);
        public void Reset() => _ban.Clear();
    }

    internal sealed class DoubleLetterTracker : IDoubleLetterTracker, IRoomStateService
    {
        private readonly HashSet<string> _played = new(StringComparer.Ordinal);

        public void Mark(AlphaChainPlayerState player) => _played.Add(player.UserId);
        public bool HasPlayed(AlphaChainPlayerState player) => _played.Contains(player.UserId);

        public void OnEraStarted(AlphaChainPlayerState player) => _played.Remove(player.UserId);
        public void Reset() => _played.Clear();
    }
}
