using KnockBox.Plugins.Analyzer;

namespace KnockBox.Plugins.AnalyzerTests;

[TestClass]
public sealed class KB1002HttpAnalyzerTests
{
    [TestMethod]
    public async Task NewHttpClient_ProducesKB1002()
    {
        var source = """
            using System.Net.Http;
            public class C { public HttpClient M() => new HttpClient(); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1002HttpAnalyzer>(
            source, "KB1002", "System.Net.Http.HttpClient");
    }

    [TestMethod]
    public async Task NewTcpClient_ProducesKB1002()
    {
        var source = """
            using System.Net.Sockets;
            public class C { public TcpClient M() => new TcpClient(); }
            """;

        await AnalyzerHarness.AssertSingleDiagnosticAsync<KB1002HttpAnalyzer>(
            source, "KB1002", "System.Net.Sockets.TcpClient");
    }

    [TestMethod]
    public async Task NonNetworkCode_ProducesNoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            public class C { public List<int> M() => new List<int>(); }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1002HttpAnalyzer>(source);
    }

    [TestMethod]
    public async Task PragmaWarningDisable_SuppressesKB1002()
    {
        var source = """
            using System.Net.Http;
            public class C {
                public HttpClient M() {
            #pragma warning disable KB1002
                    return new HttpClient();
            #pragma warning restore KB1002
                }
            }
            """;

        await AnalyzerHarness.AssertNoDiagnosticAsync<KB1002HttpAnalyzer>(source);
    }
}
