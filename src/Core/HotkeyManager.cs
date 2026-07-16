using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
    /// Erkennt Win+J vollständig im Low-Level-Keyboard-Hook.
    /// Windows 11 verwendet Win+J inzwischen selbst (Recall). Deshalb darf die
    /// Kombination nicht mehr von RegisterHotKey oder Windows ausgewertet werden.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int VkLWin = 0x5B;
        private const int VkRWin = 0x5C;
        private const int VkJ = 0x4A;
        private const int VkF15 = 0x7E;
        private const int VkEscape = 0x1B;
        private const int VkReturn = 0x0D;
        private const int LlkHfInjected = 0x10;
        private const uint KeyEventFKeyUp = 0x0002;

        private const int ShortPressMs = 200;
        private const int DoubleTapMs = 400;

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _hookProc;
        private IntPtr _hookId;

        private volatile bool _leftWinDown;
        private volatile bool _rightWinDown;
        private volatile bool _jDown;
        private volatile bool _sessionActive;
        private volatile bool _longFired;
        private volatile bool _suppressJUntilKeyUp;
        private volatile bool _ignoreCurrentRelease;
        private volatile bool _enterDown;
        private int _sessionGeneration;
        private DateTime _lastTapStarted = DateTime.MinValue;
        private int _tapCount;

        /// <summary>Vordergrundfenster exakt zum Zeitpunkt des Win+J-Drückens.</summary>
        public IntPtr ForegroundWindowOnPress { get; private set; }

        public event EventHandler<HotkeyEvent>? HotkeyFired;
        public event EventHandler? EscapePressed;
        public event EventHandler? ConfirmPressed;
        public bool SuggestionConfirmationActive { get; set; }

        public HotkeyManager()
        {
            _hookProc = HookCallback;
        }

        public void Register(Window window)
        {
            _ = window;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            _hookId = SetWindowsHookEx(
                WhKeyboardLl,
                _hookProc,
                GetModuleHandle(process.MainModule!.ModuleName),
                0);

            if (_hookId == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Der globale Lumi-Hotkey konnte nicht aktiviert werden.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            var vk = Marshal.ReadInt32(lParam, 0);
            var flags = Marshal.ReadInt32(lParam, 8);
            var isInjected = (flags & LlkHfInjected) != 0;
            var message = wParam.ToInt32();
            var isDown = message == WmKeyDown || message == WmSysKeyDown;
            var isUp = message == WmKeyUp || message == WmSysKeyUp;

            if (isInjected)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (vk == VkEscape)
            {
                if (isDown)
                    Dispatch(() => EscapePressed?.Invoke(this, EventArgs.Empty));
                if (SuggestionConfirmationActive)
                    return new IntPtr(1);
            }

            if (vk == VkReturn && SuggestionConfirmationActive)
            {
                if (isDown && !_enterDown)
                {
                    _enterDown = true;
                    Dispatch(() => ConfirmPressed?.Invoke(this, EventArgs.Empty));
                }
                if (isUp)
                    _enterDown = false;
                return new IntPtr(1);
            }

            if (vk == VkLWin)
            {
                _leftWinDown = isDown || (!isUp && _leftWinDown);
                if (isUp && _sessionActive)
                    EndSession();
            }
            else if (vk == VkRWin)
            {
                _rightWinDown = isDown || (!isUp && _rightWinDown);
                if (isUp && _sessionActive)
                    EndSession();
            }
            else if (vk == VkJ)
            {
                if (isDown)
                {
                    _jDown = true;
                    if (_sessionActive || _suppressJUntilKeyUp || IsWinPhysicallyDown())
                    {
                        if (!_sessionActive)
                            BeginSession();

                        // Sowohl das erste J als auch Auto-Repeat vollständig
                        // abfangen, damit weder Recall noch ein stray "j" erscheint.
                        return new IntPtr(1);
                    }
                }
                else if (isUp)
                {
                    _jDown = false;
                    if (_sessionActive)
                        EndSession();

                    if (_suppressJUntilKeyUp)
                    {
                        _suppressJUntilKeyUp = false;
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool IsWinPhysicallyDown() =>
            _leftWinDown ||
            _rightWinDown ||
            (GetAsyncKeyState(VkLWin) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRWin) & 0x8000) != 0;

        private void BeginSession()
        {
            ForegroundWindowOnPress = GetForegroundWindow();
            _sessionActive = true;
            _longFired = false;
            _ignoreCurrentRelease = false;
            _suppressJUntilKeyUp = true;

            // Da Windows nur noch die Win-Taste sieht, würde es beim späteren
            // Loslassen sonst das Startmenü öffnen. Ein neutrales F15 markiert
            // die Win-Sequenz als benutzt, ohne ein Fenster oder Zeichen auszulösen.
            NeutralizeWindowsKeySequence();

            var now = DateTime.UtcNow;
            if (_tapCount == 1 &&
                (now - _lastTapStarted).TotalMilliseconds < DoubleTapMs)
            {
                _tapCount = 0;
                _ignoreCurrentRelease = true;
                Interlocked.Increment(ref _sessionGeneration);
                Dispatch(() => Raise(HotkeyEvent.DoubleTapped));
                return;
            }

            _tapCount = 1;
            _lastTapStarted = now;
            var generation = Interlocked.Increment(ref _sessionGeneration);
            _ = DetectLongPressAsync(generation);
        }

        private async Task DetectLongPressAsync(int generation)
        {
            await Task.Delay(ShortPressMs).ConfigureAwait(false);

            if (!_sessionActive ||
                generation != Volatile.Read(ref _sessionGeneration) ||
                !_jDown ||
                !IsWinPhysicallyDown())
                return;

            _longFired = true;
            _tapCount = 0;
            Dispatch(() => Raise(HotkeyEvent.LongPressStart));
        }

        private void EndSession()
        {
            if (!_sessionActive)
                return;

            _sessionActive = false;
            Interlocked.Increment(ref _sessionGeneration);

            if (_ignoreCurrentRelease)
            {
                _ignoreCurrentRelease = false;
                return;
            }

            if (_longFired)
                Dispatch(() => Raise(HotkeyEvent.Released));
            else
                Dispatch(() => Raise(HotkeyEvent.ShortPressed));
        }

        private static void NeutralizeWindowsKeySequence()
        {
            keybd_event(VkF15, 0, 0, UIntPtr.Zero);
            keybd_event(VkF15, 0, KeyEventFKeyUp, UIntPtr.Zero);
        }

        private static void Dispatch(Action action) =>
            WpfApp.Current?.Dispatcher.BeginInvoke(action);

        private void Raise(HotkeyEvent evt) =>
            HotkeyFired?.Invoke(this, evt);

        public void Dispose()
        {
            Interlocked.Increment(ref _sessionGeneration);
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
