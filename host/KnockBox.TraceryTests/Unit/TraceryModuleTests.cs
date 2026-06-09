using System.Linq;
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
            // Shippable as of Milestone 08 — the game is no longer work-in-progress.
            Assert.IsFalse(module.Manifest.WorkInProgress);
        }

        [TestMethod]
        public void Manifest_DeclaresClientTriSplit()
        {
            // The game UI moved to the WASM client, so the module no longer overrides
            // GetCustomHeader; the manifest instead points the loader at the client assembly.
            var module = new TraceryModule();

            Assert.AreEqual("KnockBox.Tracery.Client", module.Manifest.ClientAssembly);
            Assert.IsTrue(module.Manifest.ClientContracts.Contains("KnockBox.Tracery.Contracts"));
        }
    }
}
