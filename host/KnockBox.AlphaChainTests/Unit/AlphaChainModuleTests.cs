using KnockBox.AlphaChain;

namespace KnockBox.AlphaChain.Tests.Unit
{
    [TestClass]
    public class AlphaChainModuleTests
    {
        [TestMethod]
        public void Manifest_LoadsFromEmbeddedResource()
        {
            var module = new AlphaChainModule();

            Assert.AreEqual("Alpha Chain", module.Manifest.Name);
            Assert.AreEqual("alpha-chain", module.Manifest.RouteIdentifier);
            Assert.AreEqual("KnockBox.AlphaChain", module.Manifest.EntryAssembly);
            Assert.IsTrue(module.Manifest.WorkInProgress);
        }

        [TestMethod]
        public void GetCustomHeader_ReturnsNonNullFragment()
        {
            var module = new AlphaChainModule();

            Assert.IsNotNull(module.GetCustomHeader());
        }
    }
}
