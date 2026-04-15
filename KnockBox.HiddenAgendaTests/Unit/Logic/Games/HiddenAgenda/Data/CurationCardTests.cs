using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.Data;

[TestClass]
public class CurationCardTests
{
    private class FakeRng : IRandomNumberService
    {
        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast) => 0;
        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast) => inclusiveMin;
        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast) => new byte[length];
    }

    [TestMethod]
    public void VerifyWingPools()
    {
        foreach (Wing wing in new[] { Wing.GrandHall, Wing.ModernWing, Wing.SculptureGarden, Wing.RestorationRoom })
        {
            var pool = CurationCardPool.GetPool(wing);
            Assert.IsTrue(pool.Count > 0, $"Pool for {wing} should not be empty");
            
            // Verify distributions (roughly)
            int acquireCount = pool.Count(c => c.Type == CurationCardType.Acquire);
            int removeCount = pool.Count(c => c.Type == CurationCardType.Remove);
            int tradeCount = pool.Count(c => c.Type == CurationCardType.Trade);

            double acquirePct = (double)acquireCount / pool.Count;
            Assert.IsTrue(acquirePct >= 0.4, $"Acquire percentage too low for {wing}: {acquirePct}");
            
            foreach (var card in pool)
            {
                if (card.Type == CurationCardType.Trade)
                {
                    Assert.IsNotNull(card.AlternateEffects, $"Trade card '{card.Description}' should have AlternateEffects");
                }
                
                if (card.Type == CurationCardType.Acquire)
                {
                    Assert.IsTrue(card.Effects.All(e => e.Delta > 0), $"Acquire card '{card.Description}' should have positive deltas");
                }

                if (card.Type == CurationCardType.Remove)
                {
                    Assert.IsTrue(card.Effects.All(e => e.Delta < 0), $"Remove card '{card.Description}' should have negative deltas");
                }
            }
        }
    }

    [TestMethod]
    public void DrawThree_ReturnsExactlyThree()
    {
        var rng = new FakeRng();
        var drawn = CurationCardPool.DrawThree(rng, Wing.GrandHall);
        Assert.AreEqual(3, drawn.Count);
    }
}
