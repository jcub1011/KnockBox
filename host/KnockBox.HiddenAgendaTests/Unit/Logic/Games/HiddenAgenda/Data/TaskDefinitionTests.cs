using System.Collections.Generic;
using System.Linq;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.Data;

[TestClass]
public class TaskDefinitionTests
{
    private class FakeRng : IRandomNumberService
    {
        public int GetRandomInt(int exclusiveMax, RandomType type = RandomType.Fast) => 0;
        public int GetRandomInt(int inclusiveMin, int exclusiveMax, RandomType type = RandomType.Fast) => inclusiveMin;
        public byte[] GetRandomBytes(int length, RandomType type = RandomType.Fast) => new byte[length];
    }

    [TestMethod]
    public void AllTasks_CorrectCountAndUniqueIds()
    {
        Assert.AreEqual(31, TaskPool.AllTasks.Count);
        
        var ids = TaskPool.AllTasks.Select(t => t.Id).ToList();
        CollectionAssert.AllItemsAreUnique(ids);
    }

    [TestMethod]
    public void AllTasks_VerifyCategoriesAndDifficulty()
    {
        var categoryCounts = TaskPool.AllTasks.GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.Count());
        
        Assert.AreEqual(7, categoryCounts[TaskCategory.Devotion]);
        Assert.AreEqual(6, categoryCounts[TaskCategory.Neglect]);
        Assert.AreEqual(6, categoryCounts[TaskCategory.Style]);
        Assert.AreEqual(6, categoryCounts[TaskCategory.Movement]);
        Assert.AreEqual(6, categoryCounts[TaskCategory.Rivalry]);

        foreach (var task in TaskPool.AllTasks)
        {
            switch (task.Category)
            {
                case TaskCategory.Devotion:
                    Assert.AreEqual(TaskDifficulty.Easy, task.Difficulty);
                    Assert.AreEqual(1, task.PointValue);
                    break;
                case TaskCategory.Neglect:
                    Assert.AreEqual(TaskDifficulty.Hard, task.Difficulty);
                    Assert.AreEqual(3, task.PointValue);
                    break;
                case TaskCategory.Style:
                    Assert.AreEqual(TaskDifficulty.Medium, task.Difficulty);
                    Assert.AreEqual(2, task.PointValue);
                    break;
                case TaskCategory.Movement:
                    Assert.AreEqual(TaskDifficulty.Medium, task.Difficulty);
                    Assert.AreEqual(2, task.PointValue);
                    break;
                case TaskCategory.Rivalry:
                    Assert.AreEqual(TaskDifficulty.Hard, task.Difficulty);
                    Assert.AreEqual(3, task.PointValue);
                    break;
            }
        }
    }

    [TestMethod]
    public void GetPoolForPlayerCount_ThreePlayers_ExcludesRivalry()
    {
        var pool = TaskPool.GetPoolForPlayerCount(3);
        Assert.AreEqual(25, pool.Count);
        Assert.IsTrue(pool.All(t => t.Category != TaskCategory.Rivalry));
    }

    [TestMethod]
    public void GetPoolForPlayerCount_FourPlayers_HasThirtyTasks()
    {
        var pool = TaskPool.GetPoolForPlayerCount(4);
        Assert.AreEqual(30, pool.Count);
    }

    [TestMethod]
    public void DrawTasks_DrawsCorrectCountWithoutReplacement()
    {
        var rng = new FakeRng();
        var pool = TaskPool.AllTasks;
        int count = 10;
        
        var drawn = TaskPool.DrawTasks(rng, pool, count);
        
        Assert.AreEqual(count, drawn.Count);
        CollectionAssert.AllItemsAreUnique(drawn.Select(t => t.Id).ToList());
    }
}
