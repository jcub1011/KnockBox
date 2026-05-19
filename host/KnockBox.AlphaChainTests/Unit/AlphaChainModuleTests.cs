using KnockBox.AlphaChain;
using KnockBox.Core.Plugins;

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
        }

        [TestMethod]
        public void GetButtonContent_ReturnsNull_SoHostRendersFallbackTile()
        {
            IGameModule module = new AlphaChainModule();

            Assert.IsNull(module.GetButtonContent());
        }

        [TestMethod]
        public void GetCustomHeader_ReturnsNonNullFragment()
        {
            var module = new AlphaChainModule();

            Assert.IsNotNull(module.GetCustomHeader());
        }
    }
}
