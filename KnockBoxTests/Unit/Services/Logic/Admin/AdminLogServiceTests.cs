using KnockBox.Services.Logic.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnockBox.Tests.Unit.Services.Logic.Admin;

[TestClass]
// AdminLogService reads from a hardcoded AppContext.BaseDirectory/logs
// directory, so each test seeds files there. We run sequentially inside
// the class to avoid cross-test filename collisions (MSTestSettings.cs
// parallelizes at method level assembly-wide).
[DoNotParallelize]
public sealed class AdminLogServiceTests
{
    private static readonly string LogsDir = Path.Combine(AppContext.BaseDirectory, "logs");

    // Class-wide monotonic counter, folded into the 8-digit log-file date
    // slot so each test gets a unique, regex-valid filename.
    private static int _seedCounter;

    private readonly List<string> _createdFiles = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _createdFiles)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void GetValidatedAbsolutePath_RejectsPathTraversal()
    {
        var svc = CreateService();

        Assert.IsNull(svc.GetValidatedAbsolutePath("../appsettings.json"));
        Assert.IsNull(svc.GetValidatedAbsolutePath("..\\appsettings.json"));
        Assert.IsNull(svc.GetValidatedAbsolutePath("subdir/knockbox-20260101.log"));
        Assert.IsNull(svc.GetValidatedAbsolutePath("subdir\\knockbox-20260101.log"));
        Assert.IsNull(svc.GetValidatedAbsolutePath("knockbox-.log")); // missing date
        Assert.IsNull(svc.GetValidatedAbsolutePath("other.log"));
        Assert.IsNull(svc.GetValidatedAbsolutePath(""));
        Assert.IsNull(svc.GetValidatedAbsolutePath("   "));
    }

    [TestMethod]
    public void ReadPage_UnknownFile_ReturnsNull()
    {
        var svc = CreateService();
        var result = svc.ReadPage("knockbox-19990101.log", 0, 100);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ReadPage_PaginatesContent()
    {
        var (fileName, _) = SeedLog(lineCount: 250);

        var svc = CreateService();

        var page0 = svc.ReadPage(fileName, 0, 100);
        var page1 = svc.ReadPage(fileName, 1, 100);
        var page2 = svc.ReadPage(fileName, 2, 100);

        Assert.IsNotNull(page0);
        Assert.IsNotNull(page1);
        Assert.IsNotNull(page2);
        Assert.AreEqual(100, page0.Lines.Count);
        Assert.AreEqual(100, page1.Lines.Count);
        Assert.AreEqual(50, page2.Lines.Count);
        Assert.AreEqual(250, page0.TotalLines);
        Assert.AreEqual("line 0", page0.Lines[0]);
        Assert.AreEqual("line 99", page0.Lines[99]);
        Assert.AreEqual("line 100", page1.Lines[0]);
        Assert.AreEqual("line 249", page2.Lines[^1]);
    }

    [TestMethod]
    public void TailSince_ReturnsNewContent()
    {
        var (fileName, path) = SeedLog(lineCount: 10);

        var svc = CreateService();
        var initial = svc.TailSince(fileName, 0);
        Assert.IsNotNull(initial);
        Assert.AreEqual(10, initial.Lines.Count);

        // Append new lines.
        File.AppendAllLines(path, new[] { "line 10", "line 11" });

        var tail = svc.TailSince(fileName, initial.NewOffset);
        Assert.IsNotNull(tail);
        Assert.AreEqual(2, tail.Lines.Count);
        Assert.AreEqual("line 10", tail.Lines[0]);
        Assert.AreEqual("line 11", tail.Lines[1]);
    }

    [TestMethod]
    public void ListFiles_ReturnsOnlyKnockboxLogs()
    {
        var (knockbox, _) = SeedLog(lineCount: 1);
        var stray = Path.Combine(LogsDir, "audit.log");
        Directory.CreateDirectory(LogsDir);
        File.WriteAllText(stray, "should be ignored");
        _createdFiles.Add(stray);

        var svc = CreateService();
        var files = svc.ListFiles();

        Assert.IsTrue(files.Any(f => f.Name == knockbox),
            $"Expected {knockbox} in listing, got: {string.Join(",", files.Select(f => f.Name))}");
        Assert.IsFalse(files.Any(f => f.Name == "audit.log"));
    }

    private IAdminLogService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new KnockBox.Admin.AdminOptions
        {
            LogDirectory = LogsDir
        });
        return new AdminLogService(options, NullLogger<AdminLogService>.Instance);
    }

    private (string fileName, string path) SeedLog(int lineCount)
    {
        Directory.CreateDirectory(LogsDir);

        // Use a future year plus a per-class atomic counter so:
        //  (a) the filename is guaranteed unique within the test run, and
        //  (b) it never overlaps a real Serilog rolling file the host
        //      might have left behind in this directory.
        var counter = Interlocked.Increment(ref _seedCounter);
        var fileName = $"knockbox-2099{counter:D4}.log";

        var path = Path.Combine(LogsDir, fileName);
        var lines = Enumerable.Range(0, lineCount).Select(i => $"line {i}");
        File.WriteAllLines(path, lines);
        _createdFiles.Add(path);

        return (fileName, path);
    }
}
