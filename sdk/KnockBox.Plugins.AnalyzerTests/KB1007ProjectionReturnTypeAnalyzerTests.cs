using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1007ProjectionReturnTypeAnalyzerTests
{
    private const string ProjectorBase = """
        namespace KnockBox.Core.Services.State.Games.Shared { public abstract class AbstractGameState { } }
        namespace KnockBox.Core.Services.State.Games.Shared.Projection
        {
            public abstract class AbstractStateProjector<TState, TView> { }
        }
        """;

    [TestMethod]
    public async Task ServerOnlyViewType_ProducesKB1007_OnServer()
    {
        var source = ProjectorBase + """
            namespace MyGame
            {
                public class MyState { }
                public class P : KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector<
                    MyState, KnockBox.Core.Services.State.Games.Shared.AbstractGameState> { }
            }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1007ProjectionReturnTypeAnalyzer>(
            source, WasmAnalyzerOptions.Server,
            "KB1007", "KnockBox.Core.Services.State.Games.Shared.AbstractGameState");
    }

    [TestMethod]
    public async Task ServerOnlyViewType_ProducesKB1007_WhenKindUnset()
    {
        // The server rule defaults on for projects that omit the property.
        var source = ProjectorBase + """
            namespace MyGame
            {
                public class MyState { }
                public class P : KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector<
                    MyState, KnockBox.Core.Services.State.Games.Shared.AbstractGameState> { }
            }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1007ProjectionReturnTypeAnalyzer>(
            source, "KB1007", "AbstractGameState");
    }

    [TestMethod]
    public async Task ContractsViewType_ProducesNoDiagnostic()
    {
        var source = ProjectorBase + """
            namespace MyGame { public class MyState { } }
            namespace MyGame.Contracts { public class GameView { } }
            namespace MyGame2
            {
                public class P : KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector<
                    MyGame.MyState, MyGame.Contracts.GameView> { }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1007ProjectionReturnTypeAnalyzer>(
            source, WasmAnalyzerOptions.Server);
    }

    [TestMethod]
    public async Task ClientProject_DoesNotFire()
    {
        var source = ProjectorBase + """
            namespace MyGame
            {
                public class MyState { }
                public class P : KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector<
                    MyState, KnockBox.Core.Services.State.Games.Shared.AbstractGameState> { }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1007ProjectionReturnTypeAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1007()
    {
        var source = ProjectorBase + """
            namespace MyGame
            {
                public class MyState { }
            #pragma warning disable KB1007
                public class P : KnockBox.Core.Services.State.Games.Shared.Projection.AbstractStateProjector<
                    MyState, KnockBox.Core.Services.State.Games.Shared.AbstractGameState> { }
            #pragma warning restore KB1007
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1007ProjectionReturnTypeAnalyzer>(
            source, WasmAnalyzerOptions.Server);
    }
}
