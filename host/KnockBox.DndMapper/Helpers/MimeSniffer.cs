namespace KnockBox.DndMapper.Helpers
{
    /// Tight allow-list of image MIME types accepted by the DnD Mapper image
    /// upload path. SVG is deliberately rejected: its content can carry embedded
    /// scripts that browsers will execute when the image is rendered inline.
    internal static class MimeSniffer
    {
        public static string? Detect(ReadOnlySpan<byte> head)
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (head.Length >= 8
                && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
                && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
                return "image/png";

            // JPEG: FF D8 FF
            if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
                return "image/jpeg";

            // WebP: "RIFF" .... "WEBP"
            if (head.Length >= 12
                && head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
                && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P')
                return "image/webp";

            return null;
        }

        public static string ExtensionFor(string mime) => mime switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/webp" => "webp",
            _ => "bin",
        };

        public static string? ContentTypeForExtension(string relativePath)
        {
            int dot = relativePath.LastIndexOf('.');
            if (dot < 0) return null;
            return relativePath[(dot + 1)..].ToLowerInvariant() switch
            {
                "png" => "image/png",
                "jpg" => "image/jpeg",
                "jpeg" => "image/jpeg",
                "webp" => "image/webp",
                _ => null,
            };
        }
    }
}
