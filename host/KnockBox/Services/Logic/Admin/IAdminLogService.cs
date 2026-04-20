namespace KnockBox.Services.Logic.Admin
{
    /// <summary>
    /// Exposes the on-disk Serilog rolling files to the admin UI.
    /// Responsible for bounding file access to the configured logs
    /// directory (path-traversal defence) and for coexisting with the
    /// live Serilog writer (which opens files with shared mode).
    /// </summary>
    public interface IAdminLogService
    {
        /// <summary>
        /// Lists every <c>knockbox-*.log</c> file in the logs directory,
        /// newest first. Returns an empty list if the directory does not
        /// exist.
        /// </summary>
        IReadOnlyList<LogFileInfo> ListFiles();

        /// <summary>
        /// Reads a single page of lines from <paramref name="fileName"/>.
        /// The file name must be a bare file name matching the rolling log
        /// pattern; path components and traversal sequences are rejected.
        /// </summary>
        LogPage? ReadPage(string fileName, int pageIndex, int pageSize);

        /// <summary>
        /// Returns every line appended to <paramref name="fileName"/> after
        /// byte offset <paramref name="fromOffset"/>, along with the new
        /// tail offset. Used by the live-tail UI.
        /// </summary>
        LogTail? TailSince(string fileName, long fromOffset);

        /// <summary>
        /// Returns the absolute path of <paramref name="fileName"/> after
        /// validating it, so the download endpoint can hand it to
        /// <c>Results.File</c>. Returns <c>null</c> if the name is invalid
        /// or the file doesn't exist.
        /// </summary>
        string? GetValidatedAbsolutePath(string fileName);
    }

    public sealed record LogFileInfo(string Name, long SizeBytes, DateTime LastModifiedUtc);

    public sealed record LogPage(
        string FileName,
        int PageIndex,
        int PageSize,
        int TotalLines,
        long FileSizeBytes,
        IReadOnlyList<string> Lines);

    public sealed record LogTail(
        string FileName,
        long NewOffset,
        IReadOnlyList<string> Lines);
}
