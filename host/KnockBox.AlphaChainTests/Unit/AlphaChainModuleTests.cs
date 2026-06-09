using KnockBox.AlphaChain;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.Core.Plugins;
using KnockBox.Core.Services.Logic.Games.Engines.Shared;

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
        public void GetCustomHeader_ReturnsNull_HeaderNowLivesInTheWasmClient()
        {
            // The custom header moved to KnockBox.AlphaChain.Client (AlphaChainHeader); the server
            // module no longer overrides GetCustomHeader, so it falls back to the IGameModule
            // default (null), which is only reachable through the interface.
            IGameModule module = new AlphaChainModule();

            Assert.IsNull(module.GetCustomHeader());
        }

        [TestMethod]
        public void RegisterServices_RegistersScoreCalculatorSingleton_AndGameEngine()
        {
            var module = new AlphaChainModule();
            var registration = new RecordingRegistration();

            module.RegisterServices(registration);

            CollectionAssert.Contains(
                registration.Singletons,
                (typeof(IEngineEvaluator), typeof(EngineEvaluator)),
                "IEngineEvaluator must be registered as a singleton backed by EngineEvaluator.");
            CollectionAssert.Contains(
                registration.Singletons,
                (typeof(IModifierCardFactory), typeof(ModifierCardFactory)),
                "IModifierCardFactory must be registered as a singleton backed by ModifierCardFactory.");
            CollectionAssert.Contains(
                registration.GameEngines,
                typeof(AlphaChainGameEngine),
                "The Alpha Chain engine must be registered via AddGameEngine.");
            Assert.AreEqual(1, registration.GameEngines.Count, "Exactly one engine registration is expected.");
        }

        /// <summary>Minimal <see cref="IPluginRegistration"/> that records what a module registers.</summary>
        private sealed class RecordingRegistration : IPluginRegistration
        {
            public List<(Type Service, Type Implementation)> Singletons { get; } = new();
            public List<Type> GameEngines { get; } = new();

            public IPluginManifest Manifest => throw new NotSupportedException();

            public void AddGameEngine<TEngine>() where TEngine : AbstractGameEngine
                => GameEngines.Add(typeof(TEngine));

            public void AddSingleton<TService, TImplementation>()
                where TService : class
                where TImplementation : class, TService
                => Singletons.Add((typeof(TService), typeof(TImplementation)));

            public void AddScoped<TService, TImplementation>()
                where TService : class
                where TImplementation : class, TService { }

            public void AddTransient<TService, TImplementation>()
                where TService : class
                where TImplementation : class, TService { }

            public void AddSingleton<TService>(Func<IPluginContext, TService> factory) where TService : class { }
            public void AddScoped<TService>(Func<IPluginContext, TService> factory) where TService : class { }
            public void AddTransient<TService>(Func<IPluginContext, TService> factory) where TService : class { }
        }
    }
}
