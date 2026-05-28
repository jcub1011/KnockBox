using KnockBox.Tracery;

namespace KnockBox.Tracery.Tests.Unit
{
    [TestClass]
    public class TraceryModuleTests
    {
        [TestMethod]
        public void Manifest_LoadsFromEmbeddedResource()
        {
            var module = new TraceryModule();

            Assert.AreEqual("Tracery", module.Manifest.Name);
            Assert.AreEqual("tracery", module.Manifest.RouteIdentifier);
            Assert.AreEqual("KnockBox.Tracery", module.Manifest.EntryAssembly);
            Assert.IsTrue(module.Manifest.WorkInProgress);
        }

        [TestMethod]
        public void GetCustomHeader_ReturnsNonNullFragment()
        {
            var module = new TraceryModule();

            Assert.IsNotNull(module.GetCustomHeader());
        }
    }
}
