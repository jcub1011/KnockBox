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
            // M5 ships the game: the WIP flag is cleared and the final tile art is declared.
            Assert.IsFalse(module.Manifest.WorkInProgress);
            Assert.AreEqual("tile.svg", module.Manifest.TileAsset);
        }

        [TestMethod]
        public void GetCustomHeader_ReturnsNonNullFragment()
        {
            var module = new AlphaChainModule();

            Assert.IsNotNull(module.GetCustomHeader());
        }
    }
}
