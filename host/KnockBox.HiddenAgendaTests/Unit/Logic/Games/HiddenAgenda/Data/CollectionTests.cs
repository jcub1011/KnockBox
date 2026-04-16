using System.Linq;
using KnockBox.HiddenAgenda.Services.Logic.Games.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KnockBox.HiddenAgendaTests.Unit.Logic.Games.HiddenAgenda.Data;

[TestClass]
public class CollectionTests
{
    [TestMethod]
    public void VerifyCollectionDefinitions()
    {
        Assert.AreEqual(5, CollectionDefinitions.All.Count);
        
        var rm = CollectionDefinitions.Get(CollectionType.RenaissanceMasters);
        Assert.AreEqual("Renaissance Masters", rm.Name);
        Assert.AreEqual(12, rm.TargetValue);
        Assert.AreEqual(Wing.GrandHall, rm.PrimaryWing);

        var cs = CollectionDefinitions.Get(CollectionType.ContemporaryShowcase);
        Assert.AreEqual(10, cs.TargetValue);
        Assert.AreEqual(Wing.GrandHall, cs.PrimaryWing);

        var ig = CollectionDefinitions.Get(CollectionType.ImpressionistGallery);
        Assert.AreEqual(10, ig.TargetValue);
        Assert.AreEqual(Wing.ModernWing, ig.PrimaryWing);

        var mb = CollectionDefinitions.Get(CollectionType.MarbleAndBronze);
        Assert.AreEqual(8, mb.TargetValue);
        Assert.AreEqual(Wing.SculptureGarden, mb.PrimaryWing);

        var ea = CollectionDefinitions.Get(CollectionType.EmergingArtists);
        Assert.AreEqual(8, ea.TargetValue);
        Assert.AreEqual(Wing.SculptureGarden, ea.PrimaryWing);
    }

    [TestMethod]
    public void VerifyNoDuplicates()
    {
        var types = CollectionDefinitions.All.Select(c => c.Type).ToList();
        CollectionAssert.AllItemsAreUnique(types);
    }
}
