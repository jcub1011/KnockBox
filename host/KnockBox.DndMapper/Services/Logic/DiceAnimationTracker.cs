using System;
using System.Collections.Generic;

namespace KnockBox.DndMapper.Services.Logic
{
    public interface IDiceAnimationTracker
    {
        bool IsAnimating(Guid rollId);
        void MarkAnimating(Guid rollId);
        void MarkSettled(Guid rollId);
        event Action? Changed;
    }

    public sealed class DiceAnimationTracker : IDiceAnimationTracker
    {
        private readonly HashSet<Guid> _animating = new();
        private readonly object _gate = new();

        public event Action? Changed;

        public bool IsAnimating(Guid rollId)
        {
            lock (_gate) return _animating.Contains(rollId);
        }

        public void MarkAnimating(Guid rollId)
        {
            bool added;
            lock (_gate) added = _animating.Add(rollId);
            if (added) Changed?.Invoke();
        }

        public void MarkSettled(Guid rollId)
        {
            bool removed;
            lock (_gate) removed = _animating.Remove(rollId);
            if (removed) Changed?.Invoke();
        }
    }
}
