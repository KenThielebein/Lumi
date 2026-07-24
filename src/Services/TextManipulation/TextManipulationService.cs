using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using InputSimulatorStandard;
using InputSimulatorStandard.Native;
using Lumi.Services.Diagnostics;
using WpfApp       = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;

namespace Lumi.Services.TextManipulation
{
    public class TextManipulationService : ITextManipulationService
    {
        [DllImport("user32.dll")] static extern bool   PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] static extern bool   GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);
        [DllImport("user32.dll")] static extern bool   SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool   IsWindow(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int    cbSize;
            public int    flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;   // das wirklich fokussierte Kind-Fenster
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public int    rcCaretLeft, rcCaretTop, rcCaretRight, rcCaretBottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mouse;
            [FieldOffset(0)] public KEYBDINPUT keyboard;
            [FieldOffset(0)] public HARDWAREINPUT hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const int WM_COPY = 0x0301;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_TAB = 0x09;
        private const int TextBatchCharacters = 64;

        /// <summary>
        /// Ermittelt das fokussierte Kind-Fenster im Thread des angegebenen Top-Level-Fensters.
        /// Notepad (Windows 11) und viele moderne Apps haben den Edit-Control als Child-HWND.
        /// </summary>
        private static IntPtr GetFocusedChildWindow(IntPtr hwndTopLevel)
        {
            uint tid = GetWindowThreadProcessId(hwndTopLevel, out _);
            var  gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(tid, ref gui) && gui.hwndFocus != IntPtr.Zero)
                return gui.hwndFocus;
            return hwndTopLevel;
        }

        private readonly InputSimulator _input = new();

        // Clipboard NUR auf STA-UI-Thread aufrufen
        private static Task<T> OnUi<T>(Func<T> func) =>
            WpfApp.Current.Dispatcher.InvokeAsync(func).Task;

        private static Task OnUi(Action action) =>
            WpfApp.Current.Dispatcher.InvokeAsync(action).Task;

        public async Task InsertTextAtCursorAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Die Pipeline wartet bereits auf die physische Freigabe von Strg+#.
            // Ein kurzer Scheduler-/Message-Queue-Nachlauf genügt.
            await Task.Delay(30);

            // Text direkt als Unicode-Tastatureingabe senden.
            // Der Diktat- und Ersetzungspfad berührt die Zwischenablage nicht.
            // So bleibt ein zuvor kopierter Inhalt auch nach dem Diktat erhalten.
            await TypeUnicodeTextAsync(text);
        }

        private static async Task TypeUnicodeTextAsync(string text)
        {
            var inputs = new List<INPUT>(TextBatchCharacters * 2);
            var charactersInBatch = 0;

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];

                if (character == '\r')
                {
                    if (index + 1 < text.Length && text[index + 1] == '\n')
                        index++;
                    AddVirtualKey(inputs, VK_RETURN);
                }
                else if (character == '\n')
                {
                    AddVirtualKey(inputs, VK_RETURN);
                }
                else if (character == '\t')
                {
                    AddVirtualKey(inputs, VK_TAB);
                }
                else
                {
                    AddUnicodeCharacter(inputs, character);
                }

                charactersInBatch++;
                if (charactersInBatch < TextBatchCharacters)
                    continue;

                await SendInputBatchAsync(inputs);
                inputs.Clear();
                charactersInBatch = 0;
                await Task.Delay(1);
            }

            if (inputs.Count > 0)
                await SendInputBatchAsync(inputs);
        }

        private static void AddUnicodeCharacter(List<INPUT> inputs, char character)
        {
            inputs.Add(CreateKeyboardInput(0, character, KEYEVENTF_UNICODE));
            inputs.Add(CreateKeyboardInput(
                0, character, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        private static void AddVirtualKey(List<INPUT> inputs, ushort virtualKey)
        {
            inputs.Add(CreateKeyboardInput(virtualKey, '\0', 0));
            inputs.Add(CreateKeyboardInput(virtualKey, '\0', KEYEVENTF_KEYUP));
        }

        private static INPUT CreateKeyboardInput(
            ushort virtualKey, char scanCode, uint flags) =>
            new()
            {
                type = INPUT_KEYBOARD,
                data = new INPUTUNION
                {
                    keyboard = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = scanCode,
                        dwFlags = flags
                    }
                }
            };

        private static async Task SendInputBatchAsync(List<INPUT> inputs)
        {
            var batch = inputs.ToArray();
            var offset = 0;
            var lastError = 0;

            for (var attempt = 1; attempt <= 3 && offset < batch.Length; attempt++)
            {
                INPUT[] remaining;
                if (offset == 0)
                {
                    remaining = batch;
                }
                else
                {
                    remaining = new INPUT[batch.Length - offset];
                    Array.Copy(batch, offset, remaining, 0, remaining.Length);
                }

                var sent = SendInput(
                    (uint)remaining.Length,
                    remaining,
                    Marshal.SizeOf<INPUT>());
                lastError = Marshal.GetLastWin32Error();
                offset += (int)sent;

                if (offset < batch.Length && attempt < 3)
                    await Task.Delay(attempt * 5);
            }

            if (offset != batch.Length)
            {
                LumiDiagnostics.Write(
                    "send_input_partial",
                    ("expected_events", batch.Length),
                    ("sent_events", offset),
                    ("win32_error", lastError));
                throw new Win32Exception(
                    lastError,
                    $"Der diktierte Text konnte nicht vollständig direkt eingefügt werden " +
                    $"({offset / 2} von {batch.Length / 2} Zeichenereignissen). " +
                    "Möglicherweise läuft die Zielanwendung mit Administratorrechten.");
            }
        }

        public async Task<bool> ReplaceSelectionIfMatchesAsync(
            IntPtr sourceHwnd, string expectedText, string replacement)
        {
            if (sourceHwnd == IntPtr.Zero || !IsWindow(sourceHwnd)) return false;

            SetForegroundWindow(sourceHwnd);
            await Task.Delay(120);

            var currentSelection = await GetSelectedTextAsync(sourceHwnd);
            if (!string.Equals(currentSelection, expectedText, StringComparison.Ordinal))
                return false;

            await InsertTextAtCursorAsync(replacement);
            return true;
        }

        public Task CopyTextAsync(string text) =>
            OnUi(() =>
            {
                if (!string.IsNullOrEmpty(text))
                    WpfClipboard.SetText(text);
            });

        public async Task<string?> GetSelectedTextAsync(IntPtr sourceHwnd = default)
        {
            string? previous = await OnUi(() =>
            {
                try { return WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null; }
                catch { return null; }
            });

            await OnUi(() => { try { WpfClipboard.Clear(); } catch { } });

            // ── Stufe 1: WM_COPY direkt ans fokussierte Kind-Fenster ─────────
            // Funktioniert für Notepad, Word, VS Code u.a. Win32-Edit-Controls.
            // Kein Tastatur-Event → kein Win-Key-Konflikt.
            if (sourceHwnd != IntPtr.Zero)
            {
                var target = GetFocusedChildWindow(sourceHwnd);
                PostMessage(target, WM_COPY, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(120);

                bool gotIt = await OnUi(() => WpfClipboard.ContainsText());
                if (gotIt)
                {
                    string? fast = await OnUi(() =>
                    {
                        try { return WpfClipboard.GetText(); } catch { return null; }
                    });
                    await RestoreClipboardAsync(previous);
                    return fast;
                }

                // WM_COPY hat nichts geliefert (LibreOffice, Firefox, …)
                // Clipboard für Stufe 2 wieder leeren
                await OnUi(() => { try { WpfClipboard.Clear(); } catch { } });
            }

            // ── Stufe 2: Ctrl+C ─────────────────────────────────────────────
            // Funktioniert universell, auch für LibreOffice und Browser.
            // Strg+# ist hier bereits vollständig losgelassen.
            _input.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);
            await Task.Delay(150);

            string? result = await OnUi(() =>
            {
                try { return WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null; }
                catch { return null; }
            });

            await RestoreClipboardAsync(previous);
            return result;
        }

        private static async Task RestoreClipboardAsync(string? previous)
        {
            await OnUi(() =>
            {
                try
                {
                    if (previous != null) WpfClipboard.SetText(previous);
                    else                  WpfClipboard.Clear();
                }
                catch { }
            });
        }
    }
}
