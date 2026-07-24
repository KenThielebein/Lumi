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
    /// Erkennt Strg+# vollständig im Low-Level-Keyboard-Hook.
    /// Die Rautentaste wird über ihre physische Position auf einer deutschen
    /// Tastatur erkannt, damit weder Windows- noch sprachabhängige Symbol-
    /// Kombinationen den Lumi-Hotkey übernehmen.
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(
            uint nInputs, INPUT[] pInputs, int cbSize);

        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int VkControl = 0x11;
        private const int VkLControl = 0xA2;
        private const int VkRControl = 0xA3;
        private const int VkEscape = 0x1B;
        private const int VkReturn = 0x0D;
        private const uint HashKeyScanCode = 0x2B;
        private const uint LlkHfInjected = 0x10;
        private const uint InputKeyboard = 1;
        private const uint KeyEventFKeyUp = 0x0002;
        private const uint KeyEventFScanCode = 0x0008;
        private const uint LumiInputMarkerValue = 0x4C554D49; // "LUMI"
        private static readonly UIntPtr LumiInputMarker = new(LumiInputMarkerValue);

        private const int ShortPressMs = 200;
        private const int DoubleTapMs = 400;
        private const int ChordGraceMs = 55;

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint VkCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT Keyboard;
            [FieldOffset(0)]
            public MOUSEINPUT Mouse;
            [FieldOffset(0)]
            public HARDWAREINPUT Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint Message;
            public ushort ParameterLow;
            public ushort ParameterHigh;
        }

        private readonly LowLevelKeyboardProc _hookProc;
        private readonly object _pendingHashSync = new();
        private IntPtr _hookId;

        private volatile bool _leftControlDown;
        private volatile bool _rightControlDown;
        private volatile bool _genericControlDown;
        private volatile bool _hashDown;
        private volatile bool _sessionActive;
        private volatile bool _longFired;
        private volatile bool _suppressHashUntilKeyUp;
        private volatile bool _pendingHash;
        private volatile bool _hashCommittedToSystem;
        private volatile bool _ignoreCurrentRelease;
        private volatile bool _enterDown;
        private int _sessionGeneration;
        private int _pendingHashGeneration;
        private DateTime _lastTapStarted = DateTime.MinValue;
        private int _tapCount;

        /// <summary>Vordergrundfenster exakt zum Zeitpunkt des Strg+#-Drückens.</summary>
        public IntPtr ForegroundWindowOnPress { get; private set; }

        public event EventHandler<HotkeyEvent>? HotkeyFired;
        public event EventHandler? EscapePressed;
        public event EventHandler? ConfirmPressed;
        public bool SuggestionConfirmationActive { get; set; }
        public bool AreHotkeyKeysDown =>
            IsAnyControlTrackedDown() || _hashDown;

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

            var hookData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var vk = unchecked((int)hookData.VkCode);
            var isOwnInjectedInput =
                IsOwnInjectedInput(hookData.Flags, hookData.ExtraInfo);
            var message = wParam.ToInt32();
            var isDown = message == WmKeyDown || message == WmSysKeyDown;
            var isUp = message == WmKeyUp || message == WmSysKeyUp;

            // Nur Lumi-eigene Replay-Ereignisse überspringen. Tastaturtreiber,
            // OEM-Dienste und Remoting können echte Eingaben als "injected"
            // kennzeichnen und müssen weiterhin als Benutzereingabe gelten.
            if (isOwnInjectedInput)
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

            if (IsControlKey(vk))
            {
                TrackControlKey(vk, isDown, isUp);

                if (isDown)
                    BeginPendingHashSessionIfPossible();

                if (isUp && _sessionActive)
                    EndSession();
            }
            else if (IsHashKey(hookData.ScanCode))
            {
                if (isDown)
                {
                    var wasAlreadyDown = _hashDown;
                    _hashDown = true;

                    // Nach dem kurzen Chord-Fenster bleibt eine gehaltene
                    // Rautentaste normale Texteingabe. Ein erst später gedrücktes
                    // Strg darf daraus keine Lumi-Session mehr machen.
                    if (IsHashCommittedToSystem())
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);

                    if (_sessionActive || _suppressHashUntilKeyUp)
                    {
                        // Erstes Down und Auto-Repeat vollständig abfangen.
                        return new IntPtr(1);
                    }

                    if (IsControlPhysicallyDown())
                    {
                        BeginSession();
                        return new IntPtr(1);
                    }

                    // Bei nahezu gleichzeitigem Anschlag kann # wenige
                    // Millisekunden vor Strg eintreffen. Kurz puffern und bei
                    // ausbleibendem Strg unverändert per Scan-Code nachreichen.
                    if (!wasAlreadyDown && !IsHashPending())
                        BeginPendingHash();
                    if (IsHashPending())
                        return new IntPtr(1);
                }
                else if (isUp)
                {
                    _hashDown = false;

                    if (ReplayPendingHashTapIfNeeded())
                        return new IntPtr(1);

                    if (_sessionActive)
                        EndSession();

                    if (_suppressHashUntilKeyUp)
                    {
                        _suppressHashUntilKeyUp = false;
                        return new IntPtr(1);
                    }

                    ClearHashCommittedToSystem();
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsOwnInjectedInput(uint flags, UIntPtr extraInfo) =>
            (flags & LlkHfInjected) != 0 &&
            extraInfo == LumiInputMarker;

        private static bool IsControlKey(int virtualKey) =>
            virtualKey == VkControl ||
            virtualKey == VkLControl ||
            virtualKey == VkRControl;

        private static bool IsHashKey(uint scanCode) =>
            scanCode == HashKeyScanCode;

        private void TrackControlKey(int virtualKey, bool isDown, bool isUp)
        {
            if (virtualKey == VkLControl)
                _leftControlDown = isDown || (!isUp && _leftControlDown);
            else if (virtualKey == VkRControl)
                _rightControlDown = isDown || (!isUp && _rightControlDown);
            else
                _genericControlDown = isDown || (!isUp && _genericControlDown);
        }

        private bool IsAnyControlTrackedDown() =>
            _leftControlDown || _rightControlDown || _genericControlDown;

        private bool IsControlPhysicallyDown() =>
            IsAnyControlTrackedDown() ||
            (GetAsyncKeyState(VkLControl) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkRControl) & 0x8000) != 0 ||
            (GetAsyncKeyState(VkControl) & 0x8000) != 0;

        private void BeginPendingHashSessionIfPossible()
        {
            lock (_pendingHashSync)
            {
                if (!_hashDown ||
                    !_pendingHash ||
                    _hashCommittedToSystem)
                    return;

                _pendingHash = false;
                Interlocked.Increment(ref _pendingHashGeneration);
                BeginSession();
            }
        }

        private void BeginPendingHash()
        {
            int generation;
            lock (_pendingHashSync)
            {
                _pendingHash = true;
                generation = Interlocked.Increment(ref _pendingHashGeneration);
            }
            _ = ReplayPendingHashAsync(generation);
        }

        private async Task ReplayPendingHashAsync(int generation)
        {
            await Task.Delay(ChordGraceMs).ConfigureAwait(false);

            lock (_pendingHashSync)
            {
                if (!_pendingHash ||
                    generation != Volatile.Read(ref _pendingHashGeneration) ||
                    !_hashDown ||
                    _sessionActive ||
                    IsControlPhysicallyDown())
                    return;

                _pendingHash = false;
                _hashCommittedToSystem = true;
                ReplayHashDown();
            }
        }

        private void CancelPendingHash()
        {
            lock (_pendingHashSync)
            {
                _pendingHash = false;
                Interlocked.Increment(ref _pendingHashGeneration);
            }
        }

        private bool ReplayPendingHashTapIfNeeded()
        {
            lock (_pendingHashSync)
            {
                if (!_pendingHash)
                    return false;

                _pendingHash = false;
                Interlocked.Increment(ref _pendingHashGeneration);
                ReplayHashTap();
                return true;
            }
        }

        private bool IsHashPending()
        {
            lock (_pendingHashSync)
                return _pendingHash;
        }

        private bool IsHashCommittedToSystem()
        {
            lock (_pendingHashSync)
                return _hashCommittedToSystem;
        }

        private void ClearHashCommittedToSystem()
        {
            lock (_pendingHashSync)
                _hashCommittedToSystem = false;
        }

        private void BeginSession()
        {
            ForegroundWindowOnPress = GetForegroundWindow();
            _sessionActive = true;
            _longFired = false;
            _ignoreCurrentRelease = false;
            _suppressHashUntilKeyUp = true;

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
                !_hashDown ||
                !IsControlPhysicallyDown())
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

        private static void ReplayHashDown() =>
            SendSyntheticKeys(CreateScanCodeInput(HashKeyScanCode));

        private static void ReplayHashTap()
        {
            SendSyntheticKeys(
                CreateScanCodeInput(HashKeyScanCode),
                CreateScanCodeInput(HashKeyScanCode, keyUp: true));
        }

        private static INPUT CreateScanCodeInput(uint scanCode, bool keyUp = false) =>
            new()
            {
                Type = InputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KEYBDINPUT
                    {
                        ScanCode = unchecked((ushort)scanCode),
                        Flags = KeyEventFScanCode | (keyUp ? KeyEventFKeyUp : 0),
                        ExtraInfo = LumiInputMarker
                    }
                }
            };

        private static void SendSyntheticKeys(params INPUT[] inputs)
        {
            _ = SendInput(
                unchecked((uint)inputs.Length),
                inputs,
                Marshal.SizeOf<INPUT>());
        }

        private static void Dispatch(Action action) =>
            WpfApp.Current?.Dispatcher.BeginInvoke(action);

        private void Raise(HotkeyEvent evt) =>
            HotkeyFired?.Invoke(this, evt);

        public void Dispose()
        {
            Interlocked.Increment(ref _sessionGeneration);
            CancelPendingHash();
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }
}
