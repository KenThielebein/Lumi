using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using WpfApp = System.Windows.Application;

namespace Lumi.Core
{
    public enum HotkeyEvent
    {
        ShortPressed,
        LongPressStart,
        Released,
        DoubleTapped,
        TripleTapped
    }

    /// <summary>
    /// Win+J Hotkey-Manager.
    /// Verwendet RegisterHotKey (OS-Ebene) für zuverlässige Win+J-Erkennung
    /// ohne Stray-Character-Probleme. Ein minimaler WH_KEYBOARD_LL-Hook
    /// erkennt nur das Loslassen der Tasten und Escape – er supprimiert nichts.
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll")] static extern bool   RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool   UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern short  GetAsyncKeyState(int vKey);

        private const uint MOD_WIN       = 0x0008;
        private const uint MOD_NOREPEAT  = 0x4000;
        private const int  WM_HOTKEY     = 0x0312;
        private const int  WH_KEYBOARD_LL = 13;
        private const int  WM_KEYDOWN    = 0x0100;
        private const int  WM_KEYUP      = 0x0101;
        private const int  WM_SYSKEYDOWN = 0x0104;
        private const int  WM_SYSKEYUP   = 0x0105;
        private const int  VK_LWIN       = 0x5B;
        private const int  VK_RWIN       = 0x5C;
        private const int  VK_J          = 0x4A;
        private const int  VK_ESCAPE     = 0x1B;
        private const int  VK_RETURN     = 0x0D;
        private const int  HOTKEY_ID     = 42;

        private const int ShortPressMs = 200;
        private const int DoubleTapMs  = 400;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr     _hookId = IntPtr.Zero;
        private IntPtr     _hwnd   = IntPtr.Zero;
        private HwndSource? _hwndSource;

        // Zustandsvariablen – volatile für thread-sichere Sichtbarkeit
        // zwischen Hook-Thread und Task-Thread
        private volatile bool _waitingForRelease;
        private volatile bool _longFired;
        // Unterdrückt physische J-Tastendrücke (inkl. Auto-Repeat) während
        // einer aktiven Win+J-Session – gesetzt bei WM_HOTKEY, gelöscht bei J-KeyUp.
        private volatile bool _suppressJ;
        private volatile bool _enterDown;
        private DateTime      _pressStart;
        private DateTime      _tap1     = DateTime.MinValue;
        private int           _tapCount;

        /// <summary>Vordergrundfenster exakt zum Zeitpunkt des Win+J-Drückens.</summary>
        public IntPtr ForegroundWindowOnPress { get; private set; }

        public event EventHandler<HotkeyEvent>? HotkeyFired;
        public event EventHandler?              EscapePressed;
        public event EventHandler?              ConfirmPressed;
        public bool SuggestionConfirmationActive { get; set; }

        public HotkeyManager() { _hookProc = HookCallback; }

        public void Register(Window window)
        {
            // HWND sicherstellen (Fenster muss nicht sichtbar sein)
            _hwnd = new WindowInteropHelper(window).EnsureHandle();

            // WndProc-Hook für WM_HOTKEY
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource!.AddHook(WndProc);

            // Win+J systemweit registrieren – OS supprimiert J automatisch,
            // kein Stray-Character möglich
            RegisterHotKey(_hwnd, HOTKEY_ID, MOD_WIN | MOD_NOREPEAT, (uint)VK_J);

            // Minimaler Hook NUR für Key-UP-Erkennung und Escape
            // – supprimiert NICHTS (CallNextHookEx immer aufgerufen)
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                GetModuleHandle(proc.MainModule!.ModuleName), 0);
        }

        // ── WM_HOTKEY (RegisterHotKey) ────────────────────────────────────────
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                               ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ForegroundWindowOnPress = GetForegroundWindow();
                _suppressJ = true;   // ab jetzt physische J-Events unterdrücken
                OnWinJPressed();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnWinJPressed()
        {
            var now   = DateTime.UtcNow;
            var since = (now - _tap1).TotalMilliseconds;

            // Doppel-Tap
            if (_tapCount == 1 && since < DoubleTapMs)
            {
                _tapCount          = 2;
                _waitingForRelease = false;
                Raise(HotkeyEvent.DoubleTapped);
                return;
            }

            _tapCount          = 1;
            _tap1              = now;
            _pressStart        = now;
            _longFired         = false;
            _waitingForRelease = true;

            // Long-Press-Timer: nach 200 ms prüfen ob J noch gehalten wird
            Task.Delay(ShortPressMs).ContinueWith(_ =>
            {
                if (!_waitingForRelease) return; // Key-UP-Hook hat schon gehandelt

                if ((GetAsyncKeyState(VK_J) & 0x8000) != 0)
                {
                    // J noch gedrückt → Long Press
                    _longFired = true;
                    WpfApp.Current?.Dispatcher.BeginInvoke(() => Raise(HotkeyEvent.LongPressStart));
                }
                // sonst: J schon losgelassen, Hook hat ShortPressed bereits gefeuert
            });
        }

        // ── WH_KEYBOARD_LL: nur Beobachter, kein Suppressor ──────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int  vk         = Marshal.ReadInt32(lParam, 0);
                int  msg        = wParam.ToInt32();
                bool isDown     = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool isUp       = msg == WM_KEYUP   || msg == WM_SYSKEYUP;
                int  flags      = Marshal.ReadInt32(lParam, 8);
                bool isInjected = (flags & 0x10) != 0;  // LLKHF_INJECTED

                // Escape
                if (vk == VK_ESCAPE && isDown)
                    WpfApp.Current?.Dispatcher.BeginInvoke(
                        () => EscapePressed?.Invoke(this, EventArgs.Empty));
                if (vk == VK_ESCAPE && SuggestionConfirmationActive)
                    return new IntPtr(1);

                if (vk == VK_RETURN && SuggestionConfirmationActive)
                {
                    if (isDown && !_enterDown)
                    {
                        _enterDown = true;
                        WpfApp.Current?.Dispatcher.BeginInvoke(
                            () => ConfirmPressed?.Invoke(this, EventArgs.Empty));
                    }
                    if (isUp) _enterDown = false;
                    return new IntPtr(1);
                }

                // Key-UP: Ende der Push-to-Talk-Session erkennen.
                // Injizierte Win-KeyUp-Events (z.B. F15-Trick in GetSelectedTextAsync)
                // werden ignoriert – sie dürfen die Session NICHT vorzeitig beenden.
                if (_waitingForRelease && isUp && !isInjected &&
                    (vk == VK_J || vk == VK_LWIN || vk == VK_RWIN))
                {
                    _waitingForRelease = false;
                    if (_longFired)
                        WpfApp.Current?.Dispatcher.BeginInvoke(() => Raise(HotkeyEvent.Released));
                    else
                        WpfApp.Current?.Dispatcher.BeginInvoke(() => Raise(HotkeyEvent.ShortPressed));
                }

                // J-KeyUp: Unterdrückung aufheben (KeyUp allein tippt nie ein Zeichen)
                if (vk == VK_J && isUp)
                    _suppressJ = false;

                // Physische J-KeyDown/-Repeat während aktiver Session unterdrücken.
                // Verhindert dass Auto-Repeat-Events die Selektion im Texteditor löschen.
                // Injizierte Events (LLKHF_INJECTED) werden durchgelassen,
                // damit TextManipulationService ungehindert Ctrl+C/V senden kann.
                if (_suppressJ && vk == VK_J && isDown && !isInjected)
                    return new IntPtr(1); // unterdrücken – kein CallNextHookEx
            }

            // Alles andere durchleiten – dieser Hook supprimiert grundsätzlich nichts
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void Raise(HotkeyEvent evt) =>
            HotkeyFired?.Invoke(this, evt);

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)   UnregisterHotKey(_hwnd, HOTKEY_ID);
            if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
            _hwndSource?.RemoveHook(WndProc);
        }
    }
}
