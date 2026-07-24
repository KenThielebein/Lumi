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
        private const int VkControl = 0x11;
        private const int VkEscape = 0x1B;
        private const int VkReturn = 0x0D;
        private const int LlkHfInjected = 0x10;
        private const uint KeyEventFKeyUp = 0x0002;

        private const int ShortPressMs = 200;
        private const int DoubleTapMs = 400;
        private const int ChordGraceMs = 55;

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        private readonly LowLevelKeyboardProc _hookProc;
        private readonly object _pendingJSync = new();
        private IntPtr _hookId;

        private volatile bool _leftWinDown;
        private volatile bool _rightWinDown;
        private volatile bool _jDown;
        private volatile bool _sessionActive;
        private volatile bool _longFired;
        private volatile bool _suppressJUntilKeyUp;
        private volatile bool _pendingJ;
        private volatile bool _jCommittedToSystem;
        private volatile bool _suppressLeftWinUntilKeyUp;
        private volatile bool _suppressRightWinUntilKeyUp;
        private volatile bool _ignoreCurrentRelease;
        private volatile bool _enterDown;
        private int _sessionGeneration;
        private int _pendingJGeneration;
        private DateTime _lastTapStarted = DateTime.MinValue;
        private int _tapCount;

        /// <summary>Vordergrundfenster exakt zum Zeitpunkt des Win+J-Drückens.</summary>
        public IntPtr ForegroundWindowOnPress { get; private set; }

        public event EventHandler<HotkeyEvent>? HotkeyFired;
        public event EventHandler? EscapePressed;
        public event EventHandler? ConfirmPressed;
        public bool SuggestionConfirmationActive { get; set; }
        public bool AreHotkeyKeysDown => _leftWinDown || _rightWinDown || _jDown;

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
                if (isDown && TryHandleJBeforeWindows(leftWindowsKey: true))
                    return new IntPtr(1);
                if (isUp && _sessionActive)
                    EndSession();
                if (isUp && _suppressLeftWinUntilKeyUp)
                {
                    _suppressLeftWinUntilKeyUp = false;
                    return new IntPtr(1);
                }
            }
            else if (vk == VkRWin)
            {
                _rightWinDown = isDown || (!isUp && _rightWinDown);
                if (isDown && TryHandleJBeforeWindows(leftWindowsKey: false))
                    return new IntPtr(1);
                if (isUp && _sessionActive)
                    EndSession();
                if (isUp && _suppressRightWinUntilKeyUp)
                {
                    _suppressRightWinUntilKeyUp = false;
                    return new IntPtr(1);
                }
            }
            else if (vk == VkJ)
            {
                if (isDown)
                {
                    var wasAlreadyDown = _jDown;
                    _jDown = true;

                    // Wurde ein normales J nach dem kurzen Chord-Fenster bereits
                    // an Windows weitergegeben, bleiben auch seine Auto-Repeats
                    // normale Texteingabe. Ein deutlich später gedrücktes Win darf
                    // daraus keine Lumi-Session mehr machen.
                    if (IsJCommittedToSystem())
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);

                    if (_sessionActive || _suppressJUntilKeyUp)
                    {
                        // Sowohl das erste J als auch Auto-Repeat vollständig
                        // abfangen, damit weder Recall noch ein stray "j" erscheint.
                        return new IntPtr(1);
                    }

                    if (IsWinPhysicallyDown())
                    {
                        BeginSession();
                        return new IntPtr(1);
                    }

                    // Bei nahezu gleichzeitigem Anschlag kann J wenige Millisekunden
                    // vor Win eintreffen. Ein sehr kurzes Puffern verhindert, dass das
                    // erste Zeichen sichtbar wird. Bleibt Win aus, wird J unverändert
                    // als injiziertes Ereignis nachgereicht.
                    if (!wasAlreadyDown && !IsJPending())
                        BeginPendingJ();
                    if (IsJPending())
                        return new IntPtr(1);
                }
                else if (isUp)
                {
                    _jDown = false;

                    if (ReplayPendingJTapIfNeeded())
                    {
                        return new IntPtr(1);
                    }

                    if (_sessionActive)
                        EndSession();

                    if (_suppressJUntilKeyUp)
                    {
                        _suppressJUntilKeyUp = false;
                        return new IntPtr(1);
                    }

                    ClearJCommittedToSystem();
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool IsWinPhysicallyDown() =>
            _leftWinDown ||
            _rightWinDown ||
            (GetAsyncKeyState(VkLWin) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRWin) & 0x8000) != 0;

        private bool TryHandleJBeforeWindows(bool leftWindowsKey)
        {
            var beginSession = false;
            lock (_pendingJSync)
            {
                if (!_jDown)
                    return false;

                if (_pendingJ)
                {
                    _pendingJ = false;
                    Interlocked.Increment(ref _pendingJGeneration);
                    beginSession = true;
                }
                else if (!_jCommittedToSystem)
                {
                    return false;
                }

                if (leftWindowsKey)
                    _suppressLeftWinUntilKeyUp = true;
                else
                    _suppressRightWinUntilKeyUp = true;

                // Bei einem J, das erst nach dem Chord-Fenster an Windows ging,
                // wird ein späteres Win nur abgeschirmt. So entsteht weder eine
                // versehentliche Lumi-Session noch Windows Recall beim Auto-Repeat.
                if (!beginSession)
                    return true;

                // Der Win-KeyDown wird in der umgekehrten Chord-Reihenfolge
                // ebenfalls unterdrückt. Das Replay/Session-Entscheiden bleibt
                // unter demselben Lock atomar gegenüber dem 55-ms-Timer.
                BeginSession(windowsKeyWasForwarded: false);
                return true;
            }
        }

        private void BeginPendingJ()
        {
            int generation;
            lock (_pendingJSync)
            {
                _pendingJ = true;
                generation = Interlocked.Increment(ref _pendingJGeneration);
            }
            _ = ReplayPendingJAsync(generation);
        }

        private async Task ReplayPendingJAsync(int generation)
        {
            await Task.Delay(ChordGraceMs).ConfigureAwait(false);

            lock (_pendingJSync)
            {
                if (!_pendingJ ||
                    generation != Volatile.Read(ref _pendingJGeneration) ||
                    !_jDown ||
                    _sessionActive ||
                    IsWinPhysicallyDown())
                    return;

                _pendingJ = false;
                _jCommittedToSystem = true;
                ReplayJDown();
            }
        }

        private void CancelPendingJ()
        {
            lock (_pendingJSync)
            {
                _pendingJ = false;
                Interlocked.Increment(ref _pendingJGeneration);
            }
        }

        private bool ReplayPendingJTapIfNeeded()
        {
            lock (_pendingJSync)
            {
                if (!_pendingJ)
                    return false;

                _pendingJ = false;
                Interlocked.Increment(ref _pendingJGeneration);
                ReplayJTap();
                return true;
            }
        }

        private bool IsJPending()
        {
            lock (_pendingJSync)
                return _pendingJ;
        }

        private bool IsJCommittedToSystem()
        {
            lock (_pendingJSync)
                return _jCommittedToSystem;
        }

        private void ClearJCommittedToSystem()
        {
            lock (_pendingJSync)
                _jCommittedToSystem = false;
        }

        private void BeginSession(bool windowsKeyWasForwarded = true)
        {
            ForegroundWindowOnPress = GetForegroundWindow();
            _sessionActive = true;
            _longFired = false;
            _ignoreCurrentRelease = false;
            _suppressJUntilKeyUp = true;

            // Da Windows nur noch die Win-Taste sieht, würde es beim späteren
            // Loslassen sonst das Startmenü öffnen. Ein kurzer injizierter
            // Strg-Impuls markiert die Win-Sequenz als benutzt, ohne eine sichtbare
            // Funktionstaste auszulösen.
            if (windowsKeyWasForwarded)
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
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyEventFKeyUp, UIntPtr.Zero);
        }

        private static void ReplayJDown() =>
            keybd_event(VkJ, 0, 0, UIntPtr.Zero);

        private static void ReplayJTap()
        {
            keybd_event(VkJ, 0, 0, UIntPtr.Zero);
            keybd_event(VkJ, 0, KeyEventFKeyUp, UIntPtr.Zero);
        }

        private static void Dispatch(Action action) =>
            WpfApp.Current?.Dispatcher.BeginInvoke(action);

        private void Raise(HotkeyEvent evt) =>
            HotkeyFired?.Invoke(this, evt);

        public void Dispose()
        {
            Interlocked.Increment(ref _sessionGeneration);
            CancelPendingJ();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
