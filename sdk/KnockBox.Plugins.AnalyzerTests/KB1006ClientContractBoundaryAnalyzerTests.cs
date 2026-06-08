using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1006ClientContractBoundaryAnalyzerTests
{
    private const string HubBase = """
        namespace KnockBox.Core.Client.Components { public abstract class HubLobbyPageBase<TView> { } }
        """;

    [TestMethod]
    public async Task NonContractsViewType_ProducesKB1006()
    {
        var source = HubBase + """
            namespace MyGame.Client
            {
                public class RawState { }
                public class Page : KnockBox.Core.Client.Components.HubLobbyPageBase<RawState> { }
            }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1006ClientContractBoundaryAnalyzer>(
            source, WasmAnalyzerOptions.Client, "KB1006", "MyGame.Client.RawState");
    }

    [TestMethod]
    public async Task ContractsViewType_ProducesNoDiagnostic()
    {
        var source = HubBase + """
            namespace MyGame.Contracts { public class GameView { } }
            namespace MyGame.Client
            {
                public class Page : KnockBox.Core.Client.Components.HubLobbyPageBase<MyGame.Contracts.GameView> { }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1006ClientContractBoundaryAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }

    [TestMethod]
    public async Task ServerProject_DoesNotFire()
    {
        var source = HubBase + """
            namespace MyGame.Client
            {
                public class RawState { }
                public class Page : KnockBox.Core.Client.Components.HubLobbyPageBase<RawState> { }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1006ClientContractBoundaryAnalyzer>(
            source, WasmAnalyzerOptions.Server);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1006()
    {
        var source = HubBase + """
            namespace MyGame.Client
            {
                public class RawState { }
            #pragma warning disable KB1006
                public class Page : KnockBox.Core.Client.Components.HubLobbyPageBase<RawState> { }
            #pragma warning restore KB1006
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1006ClientContractBoundaryAnalyzer>(
            source, WasmAnalyzerOptions.Client);
    }
}
