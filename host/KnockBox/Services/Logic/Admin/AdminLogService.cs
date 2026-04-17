using KnockBox.Admin;
using KnockBox.Services.Logic.Storage;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Reads files from <c>IStoragePathService.GetLogDirectory()</c> for the
    /// admin UI. Opens each file with <see cref="FileShare.ReadWrite"/> so
    /// we don't collide with Serilog's live writer (which uses
    /// <c>shared: true</c> in <c>Program.ApplySharedLoggerConfiguration</c>).
    /// </summary>
    internal sealed partial class AdminLogService : IAdminLogService
    {
        // Matches Serilog's rolling filename (knockbox-YYYYMMDD.log). Used
        // both to enumerate files and to reject arbitrary input to
        // ReadPage/TailSince/GetValidatedAbsolutePath.
        [GeneratedRegex(@"^knockbox-\d{8}\.log$", RegexOptions.CultureInvariant)]
        private static partial Regex LogFileNameRegex();

        private readonly string _logsDirectory;
        private readonly ILogger<AdminLogService> _logger;

        public AdminLogService(IStoragePathService storagePath, ILogger<AdminLogService> logger)
        {
            _logger = logger;
            _logsDirectory = storagePath.GetLogDirectory();
        }

        public IReadOnlyList<LogFileInfo> ListFiles()
        {
            if (!Directory.Exists(_logsDirectory)) return [];

            try
            {
                return Directory.EnumerateFiles(_logsDirectory, "knockbox-*.log")
                    .Select(path => new FileInfo(path))
                    .Where(fi => LogFileNameRegex().IsMatch(fi.Name))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .Select(fi => new LogFileInfo(fi.Name, fi.Length, fi.LastWriteTimeUtc))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate log files in [{Dir}].", _logsDirectory);
                return [];
            }
        }

        public LogPage? ReadPage(string fileName, int pageIndex, int pageSize)
        {
            if (pageIndex < 0) pageIndex = 0;
            if (pageSize <= 0) pageSize = 200;
            if (pageSize > 5000) pageSize = 5000;

            var absolutePath = GetValidatedAbsolutePath(fileName);
            if (absolutePath is null) return null;

            try
            {
                var lines = new List<string>();
                var totalLines = 0;
                long fileLength;
                var startLine = pageIndex * pageSize;
                var endLineExclusive = startLine + pageSize;

                using var stream = new FileStream(
                    absolutePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                fileLength = stream.Length;

                using var reader = new StreamReader(stream);
                
                // Fast-forward to start line
                while (totalLines < startLine && reader.ReadLine() is not null)
                {
                    totalLines++;
                }

                // Read actual page
                string? line;
                while (totalLines < endLineExclusive && (line = reader.ReadLine()) is not null)
                {
                    lines.Add(line);
                    totalLines++;
                }

                // Count the rest of the file
                while (reader.ReadLine() is not null)
                {
                    totalLines++;
                }

                return new LogPage(
                    Path.GetFileName(absolutePath),
                    pageIndex,
                    pageSize,
                    totalLines,
                    fileLength,
                    lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read log file [{File}] page {Page}.", fileName, pageIndex);
                return null;
            }
        }

        public LogTail? TailSince(string fileName, long fromOffset)
        {
            if (fromOffset < 0) fromOffset = 0;

            var absolutePath = GetValidatedAbsolutePath(fileName);
            if (absolutePath is null) return null;

            try
            {
                using var stream = new FileStream(
                    absolutePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                // If the file was rolled/truncated behind us, reset to start.
                if (fromOffset > stream.Length) fromOffset = 0;

                stream.Seek(fromOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);

                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) is not null)
                    lines.Add(line);

                // StreamReader may buffer past the last newline; trust the
                // stream position rather than accumulating line lengths.
                var newOffset = stream.Position;

                return new LogTail(Path.GetFileName(absolutePath), newOffset, lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to tail log file [{File}] from offset {Offset}.", fileName, fromOffset);
                return null;
            }
        }

        public string? GetValidatedAbsolutePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            // Reject directory separators, traversal, and drive specs.
            if (fileName.IndexOfAny(['/', '\\']) >= 0) return null;
            if (fileName.Contains("..")) return null;
            if (!LogFileNameRegex().IsMatch(fileName)) return null;

            var candidate = Path.Combine(_logsDirectory, fileName);

            // Defence-in-depth: ensure the resolved full path is still
            // inside the logs directory. Protects against edge cases
            // (symlinks, etc.) even though the regex already forbids
            // traversal characters.
            var fullLogsDir = Path.GetFullPath(_logsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullCandidate = Path.GetFullPath(candidate);
            if (!fullCandidate.StartsWith(fullLogsDir, StringComparison.OrdinalIgnoreCase))
                return null;

            return File.Exists(fullCandidate) ? fullCandidate : null;
        }
    }
}
