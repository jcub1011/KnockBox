using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1005ClientServerTypeReferenceAnalyzerTests
{
    private const string ServerType = """
        namespace KnockBox.Core.Services.State.Games.Shared
        {
            public abstract class AbstractGameState { }
            public static class GameApi { public static void Mutate() { } }
        }
        """;

    [TestMethod]
    public async Task ClientDerivingServerType_ProducesKB1005()
    {
        var source = ServerType + """
            namespace MyGame.Client
            {
                public class MyState : KnockBox.Core.Services.State.Games.Shared.AbstractGameState { }
            }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Client,
            "KB1005", "KnockBox.Core.Services.State.Games.Shared.AbstractGameState");
    }

    [TestMethod]
    public async Task ClientCallingServerApi_ProducesKB1005()
    {
        var source = ServerType + """
            namespace MyGame.Client
            {
                public class C { public void M() => KnockBox.Core.Services.State.Games.Shared.GameApi.Mutate(); }
            }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Client, "KB1005", "GameApi");
    }

    [TestMethod]
    public async Task ClientReferencingCoreClient_ProducesNoDiagnostic()
    {
        var source = """
            namespace KnockBox.Core.Client { public class SafeBase { } }
            namespace MyGame.Client { public class C : KnockBox.Core.Client.SafeBase { } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }

    [TestMethod]
    public async Task ClientReferencingContracts_ProducesNoDiagnostic()
    {
        var source = """
            namespace MyGame.Contracts { public class GameView { } }
            namespace MyGame.Client { public class C : MyGame.Contracts.GameView { } }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }

    [TestMethod]
    public async Task ServerProject_DoesNotFire()
    {
        // The exact source that fires under client kind must be inert under server kind.
        var source = ServerType + """
            namespace MyGame.Client
            {
                public class MyState : KnockBox.Core.Services.State.Games.Shared.AbstractGameState { }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Server);
    }

    [TestMethod]
    public async Task UnsetKind_DoesNotFire()
    {
        var source = ServerType + """
            namespace MyGame.Client
            {
                public class MyState : KnockBox.Core.Services.State.Games.Shared.AbstractGameState { }
            }
            """;

        // No global options at all → property unset → client rule stays off.
        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(source);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1005()
    {
        var source = ServerType + """
            namespace MyGame.Client
            {
                public class C
                {
                    public void M()
                    {
            #pragma warning disable KB1005
                        KnockBox.Core.Services.State.Games.Shared.GameApi.Mutate();
            #pragma warning restore KB1005
                    }
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1005ClientServerTypeReferenceAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }
}
