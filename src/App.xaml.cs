using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using Lumi.Config;
using Lumi.Core;
using Lumi.Services.AI;
using Lumi.Services.Audio;
using Lumi.Services.Diagnostics;
using Lumi.Services.Memory;
using Lumi.Services.Speech;
using Lumi.Services.TextManipulation;
using Lumi.UI;

namespace Lumi
{
    public partial class App : System.Windows.Application
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        private TaskbarIcon?          _trayIcon;
        private HotkeyManager?        _hotkeyManager;
        private ModeController        _modeController = new();
        private OverlayWindow?        _overlay;
        private PipelineOrchestrator? _pipeline;
        private AudioService?         _audioService;
        private bool                  _overlayVisible;
        private Mutex?                _singleInstanceMutex;
        private bool                  _ownsSingleInstanceMutex;
        private IntPtr                _lastTextTargetHwnd;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _singleInstanceMutex = new Mutex(true, @"Local\Lumi.SingleInstance", out var isFirstInstance);
            _ownsSingleInstanceMutex = isFirstInstance;
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }

            _overlay      = new OverlayWindow();
            _trayIcon     = BuildTrayIcon();
            _audioService = new AudioService();
            _hotkeyManager = new HotkeyManager();

            var config = ConfigManager.Load();
            LumiDiagnostics.Configure(config.EnableLogging);
            AutoStartManager.Apply(config.AutoStart);

            // First-run: open settings if no API key configured
            if (string.IsNullOrWhiteSpace(config.GroqApiKey))
            {
                var win = new SettingsWindow();
                win.ShowDialog();
                config = ConfigManager.Load();
                AutoStartManager.Apply(config.AutoStart);
                _overlay.ApplyTheme(config.OverlayTheme);
            }

            BuildPipeline(config);

            _hotkeyManager.HotkeyFired   += OnHotkeyFiredSafely;
            _hotkeyManager.EscapePressed += OnEscape;
            _hotkeyManager.ConfirmPressed += async (_, _) => await _pipeline!.AcceptSuggestionAsync();
            _hotkeyManager.Register(_overlay);

            // Gespeicherten Modus wiederherstellen
            if (Enum.TryParse<AppMode>(config.ActiveMode, out var savedMode))
            {
                _modeController.SetMode(savedMode);
                _overlay.UpdateModeChips(savedMode);
            }

            _overlay.ModeChangeRequested += (_, mode) => _modeController.SetMode(mode);
            _overlay.HandsFreeDictationRequested += OnHandsFreeDictationRequested;
            _overlay.SettingsRequested += (_, _) => OpenSettings();
            _overlay.ExitRequested += (_, _) => Shutdown();
            _overlay.SuggestionAcceptedRequested += async (_, _) => await _pipeline!.AcceptSuggestionAsync();
            _overlay.SuggestionCopyRequested += async (_, _) => await _pipeline!.CopySuggestionAsync();
            _overlay.SuggestionRetryRequested += async (_, _) => await _pipeline!.RetrySuggestionAsync();
            _overlay.SuggestionCancelled += (_, _) => _pipeline?.CancelSuggestion();
            _overlay.VocabularyLearningRequested += async (_, correction) =>
                await _pipeline!.LearnVocabularyAsync(correction.HeardAs, correction.Written);
            _overlay.SuggestionPreviewChanged += active =>
                _hotkeyManager.SuggestionConfirmationActive = active;

            _modeController.ModeChanged += (_, mode) =>
            {
                _pipeline?.CancelSuggestion();
                _overlay.UpdateModeChips(mode);
                // Modus dauerhaft speichern
                var cfg = ConfigManager.Load();
                cfg.ActiveMode = mode.ToString();
                ConfigManager.Save(cfg);
            };
        }

        private void BuildPipeline(AppConfig config)
        {
            _pipeline?.Dispose();
            LumiDiagnostics.Configure(config.EnableLogging);
            _audioService!.SilenceTimeoutMs = config.SilenceTimeoutMs;

            var memory = new MemoryService(config);
            _ = memory.InitializeAsync();

            ISpeechToText stt = string.IsNullOrWhiteSpace(config.GroqApiKey)
                ? new NullSttClient()
                : new GroqSttClient(config.GroqApiKey, memory.BuildVocabularyPrompt);

            var llm = new OpenAICompatibleProvider(
                config.CloudLLMBaseUrl,
                config.OpenRouterApiKey,
                config.CloudLLMModel,
                "OpenRouter");

            var smoothingProvider = new OpenAICompatibleProvider(
                config.CloudLLMBaseUrl,
                config.OpenRouterApiKey,
                config.CloudSmoothingModel,
                "OpenRouter-Smoother");

            ISmoothingService smoother = config.EnableSmoothing && !string.IsNullOrWhiteSpace(config.OpenRouterApiKey)
                ? new SmoothingService(smoothingProvider)
                : new PassThroughSmoother();

            ITextToSpeech tts         = new SAPITtsService();
            var           textService = new TextManipulationService();

            _pipeline = new PipelineOrchestrator(
                _audioService!, stt, smoother, llm, tts, textService, memory, _overlay!, _modeController,
                () => _hotkeyManager!.AreHotkeyKeysDown);
        }

        private void RebuildPipeline()
        {
            var config = ConfigManager.Load();
            BuildPipeline(config);
            _overlay?.ApplyTheme(config.OverlayTheme);
        }

        private void OpenSettings()
        {
            HideOverlay();
            var win = new SettingsWindow();
            if (win.ShowDialog() == true)
                RebuildPipeline();
        }

        private async void OnHotkeyFiredSafely(object? sender, HotkeyEvent evt)
        {
            try
            {
                await OnHotkeyFiredAsync(evt);
            }
            catch (Exception ex)
            {
                _overlay?.ShowError(ex.Message);
            }
        }

        private async Task OnHotkeyFiredAsync(HotkeyEvent evt)
        {
            switch (evt)
            {
                case HotkeyEvent.ShortPressed:
                    RememberTextTarget(_hotkeyManager!.ForegroundWindowOnPress);
                    ToggleOverlay();
                    break;

                case HotkeyEvent.LongPressStart:
                    var sourceHwnd = _hotkeyManager!.ForegroundWindowOnPress;
                    RememberTextTarget(sourceHwnd);
                    _pipeline!.PrepareRecording(sourceHwnd);
                    ShowOverlay();
                    await _pipeline!.StartListeningAsync();
                    break;

                case HotkeyEvent.Released:
                    await _pipeline!.StopAndProcessAsync();
                    break;

                case HotkeyEvent.DoubleTapped:
                    _modeController.CycleMode();
                    ShowOverlay();
                    break;

                case HotkeyEvent.TripleTapped:
                    break;
            }
        }

        private async void OnHandsFreeDictationRequested(object? sender, EventArgs e)
        {
            try
            {
                await StartHandsFreeDictationAsync();
            }
            catch (Exception ex)
            {
                _overlay?.ShowError(ex.Message);
            }
        }

        private void OnEscape(object? sender, EventArgs e)
        {
            if (_hotkeyManager?.SuggestionConfirmationActive == true)
            {
                _pipeline?.CancelSuggestion();
                return;
            }
            _pipeline?.CancelSuggestion();
            HideOverlay();
        }

        private async Task StartHandsFreeDictationAsync()
        {
            if (_pipeline == null) return;

            var targetHwnd = GetForegroundWindow();
            if (IsOverlayWindow(targetHwnd))
                targetHwnd = _lastTextTargetHwnd;
            else
                RememberTextTarget(targetHwnd);

            _modeController.SetMode(AppMode.Dictation);
            ShowOverlay();
            await _pipeline.StartHandsFreeDictationAsync(async () =>
            {
                if (targetHwnd == IntPtr.Zero) return;

                SetForegroundWindow(targetHwnd);
                await Task.Delay(100);
            });
        }

        private void RememberTextTarget(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero && !IsOverlayWindow(hwnd))
                _lastTextTargetHwnd = hwnd;
        }

        private bool IsOverlayWindow(IntPtr hwnd)
        {
            if (_overlay == null || hwnd == IntPtr.Zero) return false;
            return hwnd == new WindowInteropHelper(_overlay).Handle;
        }

        private void ToggleOverlay()
        {
            if (_overlayVisible) HideOverlay();
            else ShowOverlay();
        }

        private void ShowOverlay()
        {
            if (_overlayVisible) return;
            _overlayVisible = true;
            _overlay!.Show();
        }

        private void HideOverlay()
        {
            if (!_overlayVisible) return;
            _overlayVisible = false;
            _overlay!.SetState(OverlayState.Idle);
            _overlay!.Hide();
        }

        private TaskbarIcon BuildTrayIcon()
        {
            var icon = new TaskbarIcon { ToolTipText = "Lumi", Icon = CreateLumiIcon() };
            var menu = new System.Windows.Controls.ContextMenu();

            var settings = new System.Windows.Controls.MenuItem { Header = "Einstellungen" };
            settings.Click += (_, _) => OpenSettings();

            var exit = new System.Windows.Controls.MenuItem { Header = "Beenden" };
            exit.Click += (_, _) => Shutdown();

            menu.Items.Add(settings);
            menu.Items.Add(new System.Windows.Controls.Separator());
            menu.Items.Add(exit);
            icon.ContextMenu = menu;
            return icon;
        }

        private static Icon CreateLumiIcon()
        {
            using var bmp       = new Bitmap(32, 32);
            using var g         = Graphics.FromImage(bmp);
            using var brush     = new SolidBrush(Color.FromArgb(255, 255, 193, 7));
            using var font      = new Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.Black);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(brush, 2, 2, 28, 28);
            g.DrawString("L", font, textBrush, new PointF(8, 7));
            return Icon.FromHandle(bmp.GetHicon());
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyManager?.Dispose();
            _pipeline?.Dispose();
            _audioService?.Dispose();
            _trayIcon?.Dispose();
            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
