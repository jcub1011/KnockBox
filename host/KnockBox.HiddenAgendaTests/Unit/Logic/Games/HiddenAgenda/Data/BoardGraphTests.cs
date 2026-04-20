using System.Collections.Generic;
using System.Linq;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.Data;

[TestClass]
public class BoardGraphTests
{
    private BoardGraph _graph = null!;

    [TestInitialize]
    public void Setup()
    {
        _graph = BoardDefinitions.CreateGrandCircuit();
    }

    [TestMethod]
    public void CreateGrandCircuit_VerifySpaceCounts()
    {
        Assert.AreEqual(24, _graph.Spaces.Count);
        
        var wings = _graph.Spaces.Values.GroupBy(s => s.Wing).ToDictionary(g => g.Key, g => g.Count());
        
        Assert.AreEqual(5, wings[Wing.GrandHall]);
        Assert.AreEqual(5, wings[Wing.ModernWing]);
        Assert.AreEqual(5, wings[Wing.SculptureGarden]);
        Assert.AreEqual(5, wings[Wing.RestorationRoom]);
        Assert.AreEqual(4, wings[Wing.Corridor]);
    }

    [TestMethod]
    public void CreateGrandCircuit_VerifySpotTypes()
    {
        var mainLoopWings = new[] { Wing.GrandHall, Wing.ModernWing, Wing.SculptureGarden, Wing.RestorationRoom };
        
        foreach (var wing in mainLoopWings)
        {
            var wingSpaces = _graph.Spaces.Values.Where(s => s.Wing == wing).ToList();
            Assert.AreEqual(4, wingSpaces.Count(s => s.SpotType == SpotType.Curation));
            Assert.AreEqual(1, wingSpaces.Count(s => s.SpotType == SpotType.Event));
        }
    }

    [TestMethod]
    public void GetReachableSpaces_Distance1_ReturnsNeighbors()
    {
        var reachable = _graph.GetReachableSpaces(0, 1);
        
        // Space 0 is connected to 1 and 19 in the main loop
        Assert.AreEqual(2, reachable.Count);
        Assert.IsTrue(reachable.Any(s => s.Id == 1));
        Assert.IsTrue(reachable.Any(s => s.Id == 19));
    }

    [TestMethod]
    public void GetReachableSpaces_FromFork_ReturnsBothBranches()
    {
        // Space 2 is connected to 1, 3, and 20 (shortcut)
        var reachable = _graph.GetReachableSpaces(2, 1);
        
        Assert.AreEqual(3, reachable.Count);
        Assert.IsTrue(reachable.Any(s => s.Id == 1));
        Assert.IsTrue(reachable.Any(s => s.Id == 3));
        Assert.IsTrue(reachable.Any(s => s.Id == 20));
    }

    [TestMethod]
    public void GetReachableSpaces_Distance3_ReturnsCorrectSpaces()
    {
        var reachable = _graph.GetReachableSpaces(0, 3);
        
        // Distance 1: 1, 19
        // Distance 2: 2, 18
        // Distance 3: 3, 20 (from 2), 17 (from 18)
        // Note: 20 is distance 3 from 0 (0->1->2->20)
        
        Assert.IsTrue(reachable.Any(s => s.Id == 1));
        Assert.IsTrue(reachable.Any(s => s.Id == 19));
        Assert.IsTrue(reachable.Any(s => s.Id == 2));
        Assert.IsTrue(reachable.Any(s => s.Id == 18));
        Assert.IsTrue(reachable.Any(s => s.Id == 3));
        Assert.IsTrue(reachable.Any(s => s.Id == 20));
        Assert.IsTrue(reachable.Any(s => s.Id == 17));
        
        Assert.IsFalse(reachable.Any(s => s.Id == 0), "Should not include starting space");
    }

    [TestMethod]
    public void GetShortestDistance_MainLoop()
    {
        Assert.AreEqual(1, _graph.GetShortestDistance(0, 1));
        Assert.AreEqual(2, _graph.GetShortestDistance(0, 2));
        Assert.AreEqual(5, _graph.GetShortestDistance(0, 5));
    }

    [TestMethod]
    public void GetShortestDistance_Shortcut()
    {
        // Path 1 (Main Loop): 2-3-4-5-6-7-8-9-10-11-12 (Distance 10)
        // Path 2 (Shortcut): 2-20-21-12 (Distance 3)
        Assert.AreEqual(3, _graph.GetShortestDistance(2, 12));
    }
}
