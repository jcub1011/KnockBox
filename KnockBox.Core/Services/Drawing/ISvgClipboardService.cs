namespace KnockBox.Core.Services.Drawing
{
    /// <summary>
    /// Stores serialized SVG drawing content server-side under a short share code, enabling
    /// one user to copy their drawing and another user on a different device to paste it.
    /// </summary>
    public interface ISvgClipboardService
    {
        /// <summary>
        /// Stores SVG content and returns a short share code.
        /// Codes expire after a fixed TTL; the content can be retrieved by any user who has the code.
        /// </summary>
        string Store(string svgContent);

        /// <summary>
        /// Retrieves SVG content for the given share code, or <c>null</c> if the code is
        /// unknown or has expired.
        /// </summary>
        string? Retrieve(string shareCode);
    }
}
