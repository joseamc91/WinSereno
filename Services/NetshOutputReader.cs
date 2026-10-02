using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WinSereno.Services
{
    internal static class NetshOutputReader
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        // netsh can emit UTF-8 without a BOM even when the Windows OEM code page is not UTF-8.
        // Decode complete byte lines, so a multibyte character split across reads is never lost.
        // ASCII is identical in both encodings; non-UTF-8 lines use the native legacy code page.
        internal static async Task PumpAsync(Stream stream, Encoding legacy, Action<string> received)
        {
            var buffer = new byte[1024]; bool first = true; int count;
            using (var line = new MemoryStream())
            {
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    for (int i = 0; i < count; i++)
                    {
                        line.WriteByte(buffer[i]);
                        if (buffer[i] != 10) continue;
                        received(Decode(line.ToArray(), legacy, first));
                        first = false; line.SetLength(0); line.Position = 0;
                    }
                if (line.Length > 0) received(Decode(line.ToArray(), legacy, first));
            }
        }

        private static string Decode(byte[] bytes, Encoding legacy, bool first)
        {
            int offset = first && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try { return Utf8.GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException) { return legacy.GetString(bytes, offset, bytes.Length - offset); }
        }
    }
}
