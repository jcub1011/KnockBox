using System.Buffers.Binary;

namespace KnockBox.DndMapper.Services.Logic.Games.Http
{
    /// Best-effort intrinsic pixel-dimension reader for PNG, JPEG, and WebP.
    /// Operates on a head buffer captured during upload; if the dimensions
    /// cannot be located within that buffer the caller falls back to defaults.
    internal static class ImageDimensionSniffer
    {
        public static bool TryDetect(ReadOnlySpan<byte> head, string mime, out int width, out int height)
        {
            width = 0;
            height = 0;
            return mime switch
            {
                "image/png" => TryPng(head, out width, out height),
                "image/jpeg" => TryJpeg(head, out width, out height),
                "image/webp" => TryWebp(head, out width, out height),
                _ => false,
            };
        }

        private static bool TryPng(ReadOnlySpan<byte> head, out int width, out int height)
        {
            width = 0;
            height = 0;
            // PNG signature (8 bytes) + IHDR chunk: length(4) + "IHDR"(4) + width(4 BE) + height(4 BE)
            // IHDR width starts at offset 16, height at 20.
            if (head.Length < 24) return false;
            if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
                return false;
            width = BinaryPrimitives.ReadInt32BigEndian(head.Slice(16, 4));
            height = BinaryPrimitives.ReadInt32BigEndian(head.Slice(20, 4));
            return width > 0 && height > 0;
        }

        private static bool TryJpeg(ReadOnlySpan<byte> head, out int width, out int height)
        {
            width = 0;
            height = 0;
            // Start after SOI (FF D8). Scan segments until we hit a Start-Of-Frame marker
            // (FFC0–FFC3, FFC5–FFC7, FFC9–FFCB, FFCD–FFCF) and read its height/width.
            int i = 2;
            while (i + 9 < head.Length)
            {
                if (head[i] != 0xFF) return false;
                byte marker = head[i + 1];
                // Skip fill bytes (0xFF runs).
                if (marker == 0xFF) { i++; continue; }

                if (IsSofMarker(marker))
                {
                    // Segment: marker(2) + length(2) + precision(1) + height(2) + width(2)
                    height = (head[i + 5] << 8) | head[i + 6];
                    width = (head[i + 7] << 8) | head[i + 8];
                    return width > 0 && height > 0;
                }

                // Standalone markers without a length payload.
                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                {
                    i += 2;
                    continue;
                }

                int segLen = (head[i + 2] << 8) | head[i + 3];
                if (segLen < 2) return false;
                i += 2 + segLen;
            }
            return false;
        }

        private static bool IsSofMarker(byte m) =>
            (m >= 0xC0 && m <= 0xC3) ||
            (m >= 0xC5 && m <= 0xC7) ||
            (m >= 0xC9 && m <= 0xCB) ||
            (m >= 0xCD && m <= 0xCF);

        private static bool TryWebp(ReadOnlySpan<byte> head, out int width, out int height)
        {
            width = 0;
            height = 0;
            // RIFF header is 12 bytes; chunk FourCC at offset 12 distinguishes VP8 / VP8L / VP8X.
            if (head.Length < 30) return false;
            byte c12 = head[12], c13 = head[13], c14 = head[14], c15 = head[15];

            // VP8 (simple lossy): "VP8 ". Frame tag at offset 23..29.
            if (c12 == (byte)'V' && c13 == (byte)'P' && c14 == (byte)'8' && c15 == (byte)' ')
            {
                if (head.Length < 30) return false;
                // Bytes 23-25 must be 0x9D 0x01 0x2A start code.
                if (head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A) return false;
                width = ((head[27] << 8) | head[26]) & 0x3FFF;
                height = ((head[29] << 8) | head[28]) & 0x3FFF;
                return width > 0 && height > 0;
            }

            // VP8L (lossless): "VP8L". Signature 0x2F at offset 20, then 28 bits width-1 + height-1.
            if (c12 == (byte)'V' && c13 == (byte)'P' && c14 == (byte)'8' && c15 == (byte)'L')
            {
                if (head.Length < 25) return false;
                if (head[20] != 0x2F) return false;
                int b0 = head[21], b1 = head[22], b2 = head[23], b3 = head[24];
                int wMinus1 = (b0 | (b1 << 8)) & 0x3FFF;
                int hMinus1 = ((b1 >> 6) | (b2 << 2) | (b3 << 10)) & 0x3FFF;
                width = wMinus1 + 1;
                height = hMinus1 + 1;
                return width > 0 && height > 0;
            }

            // VP8X (extended): "VP8X". Canvas width-1 (24-bit LE) at offset 24, height-1 at offset 27.
            if (c12 == (byte)'V' && c13 == (byte)'P' && c14 == (byte)'8' && c15 == (byte)'X')
            {
                if (head.Length < 30) return false;
                int wMinus1 = head[24] | (head[25] << 8) | (head[26] << 16);
                int hMinus1 = head[27] | (head[28] << 8) | (head[29] << 16);
                width = wMinus1 + 1;
                height = hMinus1 + 1;
                return width > 0 && height > 0;
            }

            return false;
        }
    }
}
