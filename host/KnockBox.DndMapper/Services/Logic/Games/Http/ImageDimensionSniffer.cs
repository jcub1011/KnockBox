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
            switch (mime)
            {
                case "image/png":
                    return TryPng(head, out width, out height);
                case "image/jpeg":
                    if (!TryJpeg(head, out width, out height)) return false;
                    // EXIF Orientation 5/6/7/8 means the encoded pixel dimensions are
                    // swapped relative to the intended display orientation (very common
                    // on phone photos). Swap so the imported aspect matches what the
                    // user sees in any other viewer.
                    int orientation = TryReadJpegExifOrientation(head);
                    if (orientation >= 5 && orientation <= 8) (width, height) = (height, width);
                    return true;
                case "image/webp":
                    return TryWebp(head, out width, out height);
                default:
                    return false;
            }
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

        /// <summary>
        /// Scans JPEG APP1 segments for an EXIF block and returns the Orientation tag
        /// value (1–8) if present, or 0 if not found / unparseable. Orientation values
        /// 5–8 indicate the encoded pixel dimensions are rotated relative to display.
        /// </summary>
        private static int TryReadJpegExifOrientation(ReadOnlySpan<byte> head)
        {
            int i = 2; // past SOI
            while (i + 4 <= head.Length)
            {
                if (head[i] != 0xFF) return 0;
                byte marker = head[i + 1];
                if (marker == 0xFF) { i++; continue; }

                // Bail at SOS / SOF — EXIF must come before them.
                if (IsSofMarker(marker) || marker == 0xDA) return 0;

                if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
                { i += 2; continue; }

                int segLen = (head[i + 2] << 8) | head[i + 3];
                if (segLen < 2) return 0;
                int segEnd = i + 2 + segLen;
                if (segEnd > head.Length) return 0;

                // APP1: "Exif\0\0" then a TIFF header.
                if (marker == 0xE1 && segLen >= 14 && i + 10 <= head.Length &&
                    head[i + 4] == (byte)'E' && head[i + 5] == (byte)'x' &&
                    head[i + 6] == (byte)'i' && head[i + 7] == (byte)'f' &&
                    head[i + 8] == 0 && head[i + 9] == 0)
                {
                    int tiff = i + 10;
                    int ori = ParseTiffOrientation(head, tiff, segEnd);
                    if (ori != 0) return ori;
                }

                i = segEnd;
            }
            return 0;
        }

        private static int ParseTiffOrientation(ReadOnlySpan<byte> data, int tiffStart, int segEnd)
        {
            if (tiffStart + 8 > segEnd) return 0;
            bool le;
            if (data[tiffStart] == (byte)'I' && data[tiffStart + 1] == (byte)'I') le = true;
            else if (data[tiffStart] == (byte)'M' && data[tiffStart + 1] == (byte)'M') le = false;
            else return 0;

            int magic = ReadU16(data, tiffStart + 2, le);
            if (magic != 0x002A) return 0;

            uint ifd0Offset = ReadU32(data, tiffStart + 4, le);
            // The IFD offset is relative to the TIFF header start.
            long ifd0Start = (long)tiffStart + ifd0Offset;
            if (ifd0Start + 2 > segEnd || ifd0Start < tiffStart) return 0;

            int entryCount = ReadU16(data, (int)ifd0Start, le);
            int entriesStart = (int)ifd0Start + 2;
            for (int e = 0; e < entryCount; e++)
            {
                int entry = entriesStart + e * 12;
                if (entry + 12 > segEnd) return 0;
                int tag = ReadU16(data, entry, le);
                if (tag == 0x0112) // Orientation
                {
                    // Type SHORT (3), count 1 — value stored in the first 2 bytes of the value/offset field.
                    int ori = ReadU16(data, entry + 8, le);
                    return ori is >= 1 and <= 8 ? ori : 0;
                }
            }
            return 0;
        }

        private static int ReadU16(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
            littleEndian
                ? data[offset] | (data[offset + 1] << 8)
                : (data[offset] << 8) | data[offset + 1];

        private static uint ReadU32(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
            littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

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
