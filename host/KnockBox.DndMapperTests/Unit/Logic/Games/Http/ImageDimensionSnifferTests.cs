using KnockBox.DndMapper.Services.Logic.Games.Http;

namespace KnockBox.DndMapperTests.Unit.Logic.Games.Http
{
    [TestClass]
    public class ImageDimensionSnifferTests
    {
        private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static byte[] BuildPng(int width, int height, int totalBytes = 64)
        {
            if (totalBytes < 24) totalBytes = 24;
            var b = new byte[totalBytes];
            Array.Copy(PngMagic, b, PngMagic.Length);
            b[11] = 13;
            b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
            b[16] = (byte)((width >> 24) & 0xFF);
            b[17] = (byte)((width >> 16) & 0xFF);
            b[18] = (byte)((width >> 8) & 0xFF);
            b[19] = (byte)(width & 0xFF);
            b[20] = (byte)((height >> 24) & 0xFF);
            b[21] = (byte)((height >> 16) & 0xFF);
            b[22] = (byte)((height >> 8) & 0xFF);
            b[23] = (byte)(height & 0xFF);
            return b;
        }

        [TestMethod]
        public void Png_ValidIHDR_ReturnsDimensions()
        {
            var head = BuildPng(800, 600);
            Assert.IsTrue(ImageDimensionSniffer.TryDetect(head, "image/png", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Png_TruncatedBelow24Bytes_ReturnsFalse()
        {
            var head = new byte[20];
            Array.Copy(PngMagic, head, PngMagic.Length);
            Assert.IsFalse(ImageDimensionSniffer.TryDetect(head, "image/png", out _, out _));
        }

        [TestMethod]
        public void Png_MissingIHDRMarker_ReturnsFalse()
        {
            var head = new byte[64];
            Array.Copy(PngMagic, head, PngMagic.Length);
            // bytes 12..15 left as zero — not "IHDR"
            Assert.IsFalse(ImageDimensionSniffer.TryDetect(head, "image/png", out _, out _));
        }

        [TestMethod]
        public void Jpeg_SOF0AfterAPP0Segment_ReturnsDimensions()
        {
            // SOI (FFD8) + APP0 segment (FFE0, length 16) + SOF0 segment.
            var b = new List<byte>
            {
                0xFF, 0xD8,                          // SOI
                0xFF, 0xE0, 0x00, 0x10,              // APP0 marker + length=16
            };
            // 14 bytes of APP0 payload (length includes itself, so payload = 16 - 2 = 14).
            for (int i = 0; i < 14; i++) b.Add(0);
            // SOF0 segment. Pad past width bytes so the sniffer's `i + 9 < head.Length`
            // bound holds.
            b.AddRange(new byte[]
            {
                0xFF, 0xC0,        // SOF0
                0x00, 0x11,        // length (doesn't matter for the read)
                0x08,              // precision
                0x02, 0x58,        // height = 600
                0x03, 0x20,        // width = 800
                0x00, 0x00,        // trailing padding
            });
            Assert.IsTrue(ImageDimensionSniffer.TryDetect(b.ToArray(), "image/jpeg", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Jpeg_WithExifOrientation6_SwapsWidthAndHeight()
        {
            // Build SOI + APP1(Exif) with Orientation=6 (rotate 90° CW on display)
            // + SOF0 with encoded 800x600. Expected output after swap: 600x800.
            var b = new List<byte> { 0xFF, 0xD8 };

            // APP1 segment: marker(2) + length(2) + "Exif\0\0"(6) + TIFF header(8) + IFD0
            // IFD0: count(2) + 1 entry of 12 bytes + nextIfdOffset(4) = 18 bytes.
            // Total payload from length field onward = 2(length itself) + 6(Exif) + 8(TIFF) + 18(IFD0) = 34.
            // So length = 34. (length includes itself.)
            b.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x22 }); // APP1, length=34
            b.AddRange(new byte[] { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 });

            // TIFF header at offset (from start of TIFF): "II" (little-endian), 0x002A, IFD0 offset=8
            int tiffStart = b.Count;
            b.AddRange(new byte[] { (byte)'I', (byte)'I' });
            b.AddRange(new byte[] { 0x2A, 0x00 });                   // magic
            b.AddRange(new byte[] { 0x08, 0x00, 0x00, 0x00 });       // IFD0 offset = 8 (relative to TIFF start)
            // IFD0 (must start at tiffStart + 8 = current position):
            b.AddRange(new byte[] { 0x01, 0x00 });                    // entry count = 1
            // Entry: tag=0x0112 (Orientation), type=3 (SHORT), count=1, value=6 (rotate 90° CW)
            b.AddRange(new byte[] { 0x12, 0x01 });                    // tag (LE)
            b.AddRange(new byte[] { 0x03, 0x00 });                    // type SHORT
            b.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 });        // count
            b.AddRange(new byte[] { 0x06, 0x00, 0x00, 0x00 });        // value (orientation=6 in first 2 bytes)
            b.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });        // next IFD offset = 0

            // SOF0 segment with encoded 800x600.
            b.AddRange(new byte[]
            {
                0xFF, 0xC0,
                0x00, 0x11,        // length (filler)
                0x08,              // precision
                0x02, 0x58,        // height = 600 (encoded)
                0x03, 0x20,        // width  = 800 (encoded)
                0x00, 0x00,        // padding
            });

            Assert.IsTrue(ImageDimensionSniffer.TryDetect(b.ToArray(), "image/jpeg", out int w, out int h));
            // Orientation=6 → swap: encoded 800×600 should be reported as 600×800.
            Assert.AreEqual(600, w);
            Assert.AreEqual(800, h);
        }

        // Builds: SOI + APP1(Exif/TIFF little-endian) with a single Orientation entry +
        // SOF0(encoded 800x600). If `breakTiffMagic` is true, the TIFF magic bytes are
        // corrupted so the orientation parser bails (sniffer should fall back to encoded
        // dimensions, no swap).
        private static byte[] BuildJpegWithExifOrientation(int orientation, bool breakTiffMagic = false)
        {
            var b = new List<byte> { 0xFF, 0xD8 };
            b.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x22 }); // APP1, length=34
            b.AddRange(new byte[] { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 });
            // TIFF header: "II" little-endian, magic 0x002A, IFD0 offset = 8.
            b.AddRange(new byte[] { (byte)'I', (byte)'I' });
            b.AddRange(breakTiffMagic
                ? new byte[] { 0xFF, 0xFF }
                : new byte[] { 0x2A, 0x00 });
            b.AddRange(new byte[] { 0x08, 0x00, 0x00, 0x00 });
            // IFD0: 1 entry, Orientation tag.
            b.AddRange(new byte[] { 0x01, 0x00 });
            b.AddRange(new byte[] { 0x12, 0x01 });               // tag 0x0112 (LE)
            b.AddRange(new byte[] { 0x03, 0x00 });               // type SHORT
            b.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 });   // count
            b.AddRange(new byte[] { (byte)orientation, 0x00, 0x00, 0x00 });
            b.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });   // next IFD offset = 0

            // SOF0 with encoded 800x600.
            b.AddRange(new byte[]
            {
                0xFF, 0xC0, 0x00, 0x11, 0x08,
                0x02, 0x58, 0x03, 0x20, 0x00, 0x00,
            });
            return b.ToArray();
        }

        [TestMethod]
        public void Jpeg_WithExifOrientation1_PreservesEncodedDims()
        {
            // Orientation=1 means "as encoded" — no swap.
            Assert.IsTrue(ImageDimensionSniffer.TryDetect(
                BuildJpegWithExifOrientation(1), "image/jpeg", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Jpeg_WithExifOrientation8_SwapsWidthAndHeight()
        {
            // Orientation=8 (rotate 90° CCW on display) — encoded dims swap on output.
            Assert.IsTrue(ImageDimensionSniffer.TryDetect(
                BuildJpegWithExifOrientation(8), "image/jpeg", out int w, out int h));
            Assert.AreEqual(600, w);
            Assert.AreEqual(800, h);
        }

        [TestMethod]
        public void Jpeg_WithMalformedExif_FallsBackToEncodedDims()
        {
            // TIFF magic corrupted — orientation parser returns 0, sniffer must still
            // succeed and report the encoded SOF0 dimensions (no swap applied).
            Assert.IsTrue(ImageDimensionSniffer.TryDetect(
                BuildJpegWithExifOrientation(6, breakTiffMagic: true), "image/jpeg", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Jpeg_NoSOFInBuffer_ReturnsFalse()
        {
            // SOI + APP0 that consumes the whole buffer.
            var b = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x06, 0, 0, 0, 0 };
            Assert.IsFalse(ImageDimensionSniffer.TryDetect(b, "image/jpeg", out _, out _));
        }

        [TestMethod]
        public void Webp_VP8Lossy_ReturnsDimensions()
        {
            // 30-byte minimum for VP8 simple. Layout:
            // 0..3 "RIFF", 4..7 size, 8..11 "WEBP", 12..15 "VP8 ", 16..22 chunk size/frame tag,
            // 23..25 start code 9D 01 2A, 26..27 width LE & 0x3FFF, 28..29 height LE & 0x3FFF.
            var b = new byte[30];
            "RIFF"u8.CopyTo(b.AsSpan(0));
            "WEBP"u8.CopyTo(b.AsSpan(8));
            "VP8 "u8.CopyTo(b.AsSpan(12));
            b[23] = 0x9D; b[24] = 0x01; b[25] = 0x2A;
            // width = 800 = 0x0320
            b[26] = 0x20; b[27] = 0x03;
            // height = 600 = 0x0258
            b[28] = 0x58; b[29] = 0x02;

            Assert.IsTrue(ImageDimensionSniffer.TryDetect(b, "image/webp", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Webp_VP8L_ReturnsDimensions()
        {
            // Layout per sniffer: VP8L FourCC at 12..15, signature 0x2F at offset 20, then
            // 28 bits = width-1 (14b) | height-1 (14b) at bytes 21..24.
            var b = new byte[30];
            "RIFF"u8.CopyTo(b.AsSpan(0));
            "WEBP"u8.CopyTo(b.AsSpan(8));
            "VP8L"u8.CopyTo(b.AsSpan(12));
            b[20] = 0x2F;

            int wMinus1 = 799, hMinus1 = 599;
            // Encode 14-bit width then 14-bit height across bytes 21..24 little-endian.
            // bits 0..13 = width-1
            // bits 14..27 = height-1
            uint bits = (uint)(wMinus1 & 0x3FFF) | ((uint)(hMinus1 & 0x3FFF) << 14);
            b[21] = (byte)(bits & 0xFF);
            b[22] = (byte)((bits >> 8) & 0xFF);
            b[23] = (byte)((bits >> 16) & 0xFF);
            b[24] = (byte)((bits >> 24) & 0xFF);

            Assert.IsTrue(ImageDimensionSniffer.TryDetect(b, "image/webp", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void Webp_VP8X_ReturnsDimensions()
        {
            var b = new byte[30];
            "RIFF"u8.CopyTo(b.AsSpan(0));
            "WEBP"u8.CopyTo(b.AsSpan(8));
            "VP8X"u8.CopyTo(b.AsSpan(12));

            // canvas width-1 (24-bit LE) at 24..26; height-1 at 27..29
            int wMinus1 = 799, hMinus1 = 599;
            b[24] = (byte)(wMinus1 & 0xFF);
            b[25] = (byte)((wMinus1 >> 8) & 0xFF);
            b[26] = (byte)((wMinus1 >> 16) & 0xFF);
            b[27] = (byte)(hMinus1 & 0xFF);
            b[28] = (byte)((hMinus1 >> 8) & 0xFF);
            b[29] = (byte)((hMinus1 >> 16) & 0xFF);

            Assert.IsTrue(ImageDimensionSniffer.TryDetect(b, "image/webp", out int w, out int h));
            Assert.AreEqual(800, w);
            Assert.AreEqual(600, h);
        }

        [TestMethod]
        public void UnknownMime_ReturnsFalse()
        {
            var b = new byte[32];
            Assert.IsFalse(ImageDimensionSniffer.TryDetect(b, "image/gif", out _, out _));
        }
    }
}
