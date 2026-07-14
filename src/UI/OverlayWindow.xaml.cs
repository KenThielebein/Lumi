using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumi.Config;
using Lumi.Core;
using WpfColor     = System.Windows.Media.Color;
using WpfColors    = System.Windows.Media.Colors;
using WpfBrush     = System.Windows.Media.SolidColorBrush;
using WpfConverter = System.Windows.Media.ColorConverter;
using WpfPoint     = System.Windows.Point;
using WpfSize      = System.Windows.Size;

namespace Lumi.UI
{
    public enum OverlayState { Idle, Listening, Processing, Result, Error }

    public sealed class VocabularyCorrectionEventArgs : EventArgs
    {
        public VocabularyCorrectionEventArgs(string heardAs, string written)
        {
            HeardAs = heardAs;
            Written = written;
        }

        public string HeardAs { get; }
        public string Written { get; }
    }

    public partial class OverlayWindow : Window
    {
        public event EventHandler<AppMode>? ModeChangeRequested;
        public event EventHandler? HandsFreeDictationRequested;
        public event EventHandler? SettingsRequested;
        public event EventHandler? ExitRequested;
        public event EventHandler? SuggestionAcceptedRequested;
        public event EventHandler? SuggestionCopyRequested;
        public event EventHandler? SuggestionRetryRequested;
        public event EventHandler? SuggestionCancelled;
        public event EventHandler<VocabularyCorrectionEventArgs>? VocabularyLearningRequested;
        public event Action<bool>? SuggestionPreviewChanged;

        private OverlayState _state = OverlayState.Idle;
        private Storyboard?  _waveAnimation;
        private bool         _hasCustomPosition;
        private AppMode      _currentMode = AppMode.Dictation;
        private readonly List<DictationHistoryItem> _dictationHistory = new();
        private int          _selectedDictationHistoryIndex = -1;
        private bool         _updatingDictationHistoryEditor;
        private bool         _isCompact;
        private bool         _compactPreference;
        private bool         _suggestionPreviewVisible;
        private double       _expandedWidth;
        private double       _expandedHeight;
        public bool InsertDictationImmediately { get; private set; } = true;

        private sealed class DictationHistoryItem
        {
            public string Original { get; init; } = "";
            public string Edited { get; set; } = "";
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCHITTEST     = 0x0084;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_EXITSIZEMOVE  = 0x0232;
        private const int MA_NOACTIVATE    = 3;
        private const int HTCLIENT         = 1;
        private const int HTCAPTION        = 2;
        private const int HTBOTTOMRIGHT    = 17;

        public OverlayWindow()
        {
            InitializeComponent();

            var cfg = ConfigManager.Load();

            if (cfg.OverlayLeft >= 0 && cfg.OverlayTop >= 0)
            {
                Left = cfg.OverlayLeft;
                Top  = cfg.OverlayTop;
                _hasCustomPosition = true;
            }
            else
            {
                PositionAtBottomCenter();
            }

            _expandedWidth = Math.Max(480, cfg.OverlayWidth);
            _expandedHeight = Math.Max(220, cfg.OverlayHeight);
            Width  = _expandedWidth;
            Height = _expandedHeight;

            ApplyTheme(cfg.OverlayTheme);
            InsertDictationImmediately = cfg.InsertDictationImmediately;
            UpdateDictationInsertModeToggle();
            SetVersionText();
            BuildWaveAnimation();
            ApplyCompactMode(cfg.OverlayCompact, persist: false);
            if (!_hasCustomPosition)
                PositionAtBottomCenter();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE &&
                GetCursorPos(out var cursor) &&
                IsPointInElement(HandsFreeDictationButton, PointFromScreen(new WpfPoint(cursor.X, cursor.Y))))
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }

            if (msg == WM_NCHITTEST)
            {
                var localPoint = GetLocalPointFromLParam(lParam);
                if (IsPointInElement(ResizeGrip, localPoint) || IsInBottomRightResizeArea(localPoint))
                {
                    handled = true;
                    return new IntPtr(HTBOTTOMRIGHT);
                }

                if (IsPointInElement(ActionButtonsPanel, localPoint) ||
                    IsPointInElement(ModeChipsPanel, localPoint) ||
                    IsPointInElement(DictationHistoryPanel, localPoint) ||
                    IsPointInElement(DictationHistoryEditorPanel, localPoint) ||
                    IsPointInElement(SuggestionActionsPanel, localPoint))
                {
                    handled = true;
                    return new IntPtr(HTCLIENT);
                }

                if (IsPointInElement(MainBorder, localPoint))
                {
                    handled = true;
                    return new IntPtr(HTCAPTION);
                }
            }

            if (msg == WM_EXITSIZEMOVE)
                SavePositionAndSize();
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private void MainBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsFromInteractiveElement(e.OriginalSource as DependencyObject))
                return;

            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            e.Handled = true;
        }

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            ReleaseCapture();
            SendMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTBOTTOMRIGHT), IntPtr.Zero);
            e.Handled = true;
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton btn &&
                btn.Tag is string tag &&
                Enum.TryParse<AppMode>(tag, out var mode))
            {
                ModeChangeRequested?.Invoke(this, mode);
            }
        }

        public void UpdateModeChips(AppMode mode)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => UpdateModeChips(mode)); return; }
            _currentMode = mode;
            ChipSuggestion.IsChecked = mode == AppMode.Suggestion;
            ChipDictation.IsChecked  = mode == AppMode.Dictation;
        }

        private void SavePositionAndSize()
        {
            _hasCustomPosition = true;
            var cfg = ConfigManager.Load();
            cfg.OverlayLeft   = Left;
            cfg.OverlayTop    = Top;
            if (!_isCompact)
            {
                _expandedWidth = Width;
                _expandedHeight = Height;
                cfg.OverlayWidth  = Width;
                cfg.OverlayHeight = Height;
            }
            cfg.OverlayCompact = _compactPreference;
            ConfigManager.Save(cfg);
        }

        private void PositionAtBottomCenter()
        {
            var screen = SystemParameters.WorkArea;
            Left = (screen.Width - Width) / 2;
            Top  = screen.Bottom - Height - 24;
        }

        public void ApplyTheme(string theme)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ApplyTheme(theme)); return; }

            WpfColor bg, fg, fgDim;
            byte     bgAlpha;
            WpfColor chipActive, chipInactive, chipActiveBg, chipHoverBg;

            switch (theme)
            {
                case "Light":
                    bg = WpfColors.White; bgAlpha = 240;
                    fg    = WpfColor.FromRgb(17, 17, 17);
                    fgDim = WpfColor.FromArgb(160, 17, 17, 17);
                    chipActive   = WpfColor.FromRgb(17, 17, 17);
                    chipInactive = WpfColor.FromArgb(130, 17, 17, 17);
                    chipActiveBg = WpfColor.FromArgb(30, 17, 17, 17);
                    chipHoverBg  = WpfColor.FromArgb(15, 17, 17, 17);
                    break;
                case "Amber":
                    bg = WpfColor.FromRgb(255, 193, 7); bgAlpha = 232;
                    fg    = WpfColor.FromRgb(17, 17, 17);
                    fgDim = WpfColor.FromArgb(160, 17, 17, 17);
                    chipActive   = WpfColor.FromRgb(17, 17, 17);
                    chipInactive = WpfColor.FromArgb(130, 17, 17, 17);
                    chipActiveBg = WpfColor.FromArgb(40, 17, 17, 17);
                    chipHoverBg  = WpfColor.FromArgb(20, 17, 17, 17);
                    break;
                case "Blue":
                    bg = WpfColor.FromRgb(21, 101, 193); bgAlpha = 232;
                    fg    = WpfColors.White;
                    fgDim = WpfColor.FromArgb(128, 255, 255, 255);
                    chipActive   = WpfColor.FromRgb(255, 193, 7);
                    chipInactive = WpfColor.FromArgb(128, 255, 255, 255);
                    chipActiveBg = WpfColor.FromArgb(50, 255, 193, 7);
                    chipHoverBg  = WpfColor.FromArgb(30, 255, 255, 255);
                    break;
                default: // Dark
                    bg = WpfColors.Black; bgAlpha = 232;
                    fg    = WpfColors.White;
                    fgDim = WpfColor.FromArgb(128, 255, 255, 255);
                    chipActive   = WpfColor.FromRgb(255, 193, 7);
                    chipInactive = WpfColor.FromArgb(128, 255, 255, 255);
                    chipActiveBg = WpfColor.FromArgb(60, 255, 193, 7);
                    chipHoverBg  = WpfColor.FromArgb(30, 255, 255, 255);
                    break;
            }

            MainBorder.Background     = new WpfBrush(WpfColor.FromArgb(bgAlpha, bg.R, bg.G, bg.B));
            ModeIcon.Foreground       = new WpfBrush(fg);
            IdleText.Foreground       = new WpfBrush(fgDim);
            ProcessingText.Foreground = new WpfBrush(fg);
            ResultText.Foreground     = new WpfBrush(fg);
            ProviderIcon.Foreground   = new WpfBrush(fgDim);
            VersionText.Foreground    = new WpfBrush(fgDim);

            // Update chip DynamicResource brushes
            Resources["ChipActiveFg"]   = new WpfBrush(chipActive);
            Resources["ChipInactiveFg"] = new WpfBrush(chipInactive);
            Resources["ChipActiveBg"]   = new WpfBrush(chipActiveBg);
            Resources["ChipHoverBg"]    = new WpfBrush(chipHoverBg);

            // Tint resize grip lines
            var gripColor = theme is "Light" or "Amber" ? "#50000000" : "#50FFFFFF";
            if (ResizeGrip.Child is System.Windows.Shapes.Path path)
                path.Stroke = new WpfBrush((WpfColor)WpfConverter.ConvertFromString(gripColor)!);
        }

        public void SetState(OverlayState state)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetState(state)); return; }

            _state = state;
            if (state != OverlayState.Result && _suggestionPreviewVisible)
            {
                _suggestionPreviewVisible = false;
                SuggestionPreviewChanged?.Invoke(false);
            }

            IdleText.Visibility     = Visibility.Collapsed;
            WavePanel.Visibility    = Visibility.Collapsed;
            SpinnerPanel.Visibility = Visibility.Collapsed;
            ResultScroll.Visibility = Visibility.Collapsed;
            ErrorText.Visibility    = Visibility.Collapsed;
            DictationHistoryEditorPanel.Visibility = Visibility.Collapsed;
            SuggestionActionsPanel.Visibility = Visibility.Collapsed;

            _waveAnimation?.Stop();

            switch (state)
            {
                case OverlayState.Idle:       IdleText.Visibility     = Visibility.Visible; break;
                case OverlayState.Listening:  WavePanel.Visibility    = Visibility.Visible; _waveAnimation?.Begin(); break;
                case OverlayState.Processing: SpinnerPanel.Visibility = Visibility.Visible; break;
                case OverlayState.Result:     ResultScroll.Visibility = Visibility.Visible; break;
                case OverlayState.Error:      ErrorText.Visibility    = Visibility.Visible; break;
            }

            if (state == OverlayState.Idle && _compactPreference)
                ApplyCompactMode(true, persist: false);
        }

        public void ShowMessage(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowMessage(text)); return; }
            ResultText.Text = text;
            SuggestionStatusText.Text = "";
            SetState(OverlayState.Result);
        }

        public void ShowError(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowError(text)); return; }
            ErrorText.Text = text;
            SetState(OverlayState.Error);
        }

        public void ShowDictationResult(string text, bool openEditor)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowDictationResult(text, openEditor)); return; }

            _dictationHistory.Insert(0, new DictationHistoryItem
            {
                Original = text,
                Edited = text
            });
            if (_dictationHistory.Count > 5)
                _dictationHistory.RemoveAt(5);

            RefreshDictationHistoryButtons();
            if (openEditor)
            {
                ApplyCompactMode(false, persist: false);
                SetState(OverlayState.Result);
                ShowDictationHistoryEntry(0);
            }
            else
            {
                _selectedDictationHistoryIndex = -1;
                UpdateDictationHistoryButtonSelection();
                SetState(OverlayState.Idle);
            }
        }

        public void ShowSuggestionPreview(string text)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => ShowSuggestionPreview(text)); return; }
            ApplyCompactMode(false, persist: false);
            ResultText.Text = text;
            SetState(OverlayState.Result);
            SuggestionActionsPanel.Visibility = Visibility.Visible;
            _suggestionPreviewVisible = true;
            SuggestionPreviewChanged?.Invoke(true);
        }

        public void ShowSuggestionCopied()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ShowSuggestionCopied); return; }
            SuggestionStatusText.Text = "✓ kopiert";
        }

        public async void ShowTransientMessage(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowTransientMessage(text));
                return;
            }

            ShowMessage(text);
            await System.Threading.Tasks.Task.Delay(1400);
            SetState(OverlayState.Idle);
        }

        public void SetMode(string modeIcon)
        {
            // Mode icon is now shown via chips; kept for compatibility
        }

        public void SetProvider(bool isCloud)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => SetProvider(isCloud)); return; }
            ProviderIcon.Text = isCloud ? "☁" : "⚡";
        }

        private void HandsFreeDictationButton_Click(object sender, RoutedEventArgs e)
        {
            RequestHandsFreeDictation();
        }

        private void DictationInsertModeToggle_Click(object sender, RoutedEventArgs e)
        {
            InsertDictationImmediately = DictationInsertModeToggle.IsChecked == true;
            UpdateDictationInsertModeToggle();

            var cfg = ConfigManager.Load();
            cfg.InsertDictationImmediately = InsertDictationImmediately;
            ConfigManager.Save(cfg);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            RequestSettings();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            RequestExit();
        }

        private void SettingsButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RequestSettings();
        }

        private void HandsFreeDictationButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RequestHandsFreeDictation();
        }

        private void ExitButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            RequestExit();
        }

        private void RequestHandsFreeDictation()
        {
            HandsFreeDictationRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RequestSettings()
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void RequestExit()
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        }

        private void DictationHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button ||
                button.Tag is not string tag ||
                !int.TryParse(tag, out var index))
                return;

            ShowDictationHistoryEntry(index);
        }

        private void DictationHistoryEditor_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_updatingDictationHistoryEditor ||
                _selectedDictationHistoryIndex < 0 ||
                _selectedDictationHistoryIndex >= _dictationHistory.Count)
                return;

            _dictationHistory[_selectedDictationHistoryIndex].Edited = DictationHistoryEditor.Text;
        }

        private void CopyDictationHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(DictationHistoryEditor.Text)) return;

            try
            {
                System.Windows.Clipboard.SetText(DictationHistoryEditor.Text);
            }
            catch (Exception ex)
            {
                ShowError("Zwischenablage nicht verfügbar: " + ex.Message);
            }
        }

        private void LearnDictationCorrectionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDictationHistoryIndex < 0 ||
                _selectedDictationHistoryIndex >= _dictationHistory.Count)
                return;

            var item = _dictationHistory[_selectedDictationHistoryIndex];
            if (!TryFindSingleCorrection(item.Original, item.Edited, out var heardAs, out var written))
            {
                ShowError("Bitte nur eine Schreibweise korrigieren und erneut „Lernen“ wählen.");
                return;
            }

            var answer = System.Windows.MessageBox.Show(
                this,
                $"„{heardAs}“ künftig als „{written}“ schreiben?",
                "Korrektur lernen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                VocabularyLearningRequested?.Invoke(
                    this, new VocabularyCorrectionEventArgs(heardAs, written));
        }

        private void AcceptSuggestionButton_Click(object sender, RoutedEventArgs e) =>
            SuggestionAcceptedRequested?.Invoke(this, EventArgs.Empty);

        private void CopySuggestionButton_Click(object sender, RoutedEventArgs e) =>
            SuggestionCopyRequested?.Invoke(this, EventArgs.Empty);

        private void RetrySuggestionButton_Click(object sender, RoutedEventArgs e) =>
            SuggestionRetryRequested?.Invoke(this, EventArgs.Empty);

        private void CancelSuggestionButton_Click(object sender, RoutedEventArgs e) =>
            SuggestionCancelled?.Invoke(this, EventArgs.Empty);

        private void CompactToggleButton_Click(object sender, RoutedEventArgs e) =>
            ApplyCompactMode(!_isCompact, persist: true);

        private void ApplyCompactMode(bool compact, bool persist)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplyCompactMode(compact, persist));
                return;
            }

            if (!compact && _isCompact)
            {
                Width = _expandedWidth;
                Height = _expandedHeight;
            }
            else if (compact)
            {
                if (!_isCompact)
                {
                    _expandedWidth = Math.Max(480, Width);
                    _expandedHeight = Math.Max(220, Height);
                }
                Width = 300;
                Height = 84;
            }

            _isCompact = compact;
            if (persist)
                _compactPreference = compact;
            else if (!_compactPreference && compact && !IsLoaded)
                _compactPreference = true;

            MainBorder.Padding = compact
                ? new Thickness(12, 8, 38, 6)
                : new Thickness(20, 38, 48, 8);
            MainBorder.CornerRadius = new CornerRadius(compact ? 24 : 36);
            CompactToggleButton.Content = compact ? "▴" : "▾";
            CompactToggleButton.ToolTip = compact ? "Pille ausklappen" : "Pille einklappen";

            DictationInsertModeToggle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            HandsFreeDictationButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            SettingsButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ExitButton.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            DictationHistoryPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            VersionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ResizeGrip.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;

            if (persist)
            {
                var cfg = ConfigManager.Load();
                cfg.OverlayCompact = compact;
                cfg.OverlayWidth = _expandedWidth;
                cfg.OverlayHeight = _expandedHeight;
                ConfigManager.Save(cfg);
            }
        }

        private void SetVersionText()
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            version = string.IsNullOrWhiteSpace(version)
                ? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                : version.Split('+')[0];

            VersionText.Text = string.IsNullOrWhiteSpace(version) ? "" : $"v{version}";
        }

        private void UpdateDictationInsertModeToggle()
        {
            DictationInsertModeToggle.IsChecked = InsertDictationImmediately;
            DictationInsertModeToggle.Content = InsertDictationImmediately ? "↳" : "✎";
            DictationInsertModeToggle.ToolTip = InsertDictationImmediately
                ? "Diktat sofort am Cursor einfügen"
                : "Diktat zuerst im Editierfenster nachbearbeiten";
        }

        private void ShowDictationHistoryEntry(int index)
        {
            if (index < 0 || index >= _dictationHistory.Count) return;

            _selectedDictationHistoryIndex = index;
            _updatingDictationHistoryEditor = true;
            DictationHistoryEditor.Text = _dictationHistory[index].Edited;
            _updatingDictationHistoryEditor = false;

            ResultScroll.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;
            DictationHistoryEditorPanel.Visibility = Visibility.Visible;
            UpdateDictationHistoryButtonSelection();
        }

        private void RefreshDictationHistoryButtons()
        {
            var buttons = GetDictationHistoryButtons();
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].IsEnabled = i < _dictationHistory.Count;

            UpdateDictationHistoryButtonSelection();
        }

        private void UpdateDictationHistoryButtonSelection()
        {
            var buttons = GetDictationHistoryButtons();
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Background = i == _selectedDictationHistoryIndex
                    ? (WpfBrush)Resources["ChipActiveBg"]
                    : new WpfBrush(WpfColor.FromArgb(34, 0, 0, 0));
                buttons[i].Foreground = i == _selectedDictationHistoryIndex
                    ? (WpfBrush)Resources["ChipActiveFg"]
                    : new WpfBrush(WpfColor.FromArgb(128, 255, 255, 255));
            }
        }

        private System.Windows.Controls.Button[] GetDictationHistoryButtons() =>
            new[]
            {
                DictationHistoryButton1,
                DictationHistoryButton2,
                DictationHistoryButton3,
                DictationHistoryButton4,
                DictationHistoryButton5
            };

        private static bool TryFindSingleCorrection(
            string original, string edited, out string heardAs, out string written)
        {
            heardAs = "";
            written = "";
            var before = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var after = edited.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (before.SequenceEqual(after, StringComparer.Ordinal)) return false;

            var prefix = 0;
            while (prefix < before.Length && prefix < after.Length &&
                   string.Equals(before[prefix], after[prefix], StringComparison.Ordinal))
                prefix++;

            var suffix = 0;
            while (suffix < before.Length - prefix &&
                   suffix < after.Length - prefix &&
                   string.Equals(before[^(suffix + 1)], after[^(suffix + 1)], StringComparison.Ordinal))
                suffix++;

            heardAs = string.Join(" ", before.Skip(prefix).Take(before.Length - prefix - suffix));
            written = string.Join(" ", after.Skip(prefix).Take(after.Length - prefix - suffix));
            return heardAs.Length > 0 && written.Length > 0;
        }

        private static bool IsFromInteractiveElement(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is System.Windows.Controls.TextBox or
                    System.Windows.Controls.Primitives.ButtonBase)
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private WpfPoint GetLocalPointFromLParam(IntPtr lParam)
        {
            var value = lParam.ToInt64();
            var x = (short)(value & 0xFFFF);
            var y = (short)((value >> 16) & 0xFFFF);
            return PointFromScreen(new WpfPoint(x, y));
        }

        private bool IsInBottomRightResizeArea(WpfPoint point)
        {
            const double size = 28;
            return point.X >= ActualWidth - size &&
                   point.Y >= ActualHeight - size &&
                   point.X <= ActualWidth &&
                   point.Y <= ActualHeight;
        }

        private bool IsPointInElement(FrameworkElement element, WpfPoint windowPoint)
        {
            if (element.Visibility != Visibility.Visible ||
                element.ActualWidth <= 0 ||
                element.ActualHeight <= 0)
                return false;

            try
            {
                var topLeft = element.TransformToAncestor(this).Transform(new WpfPoint(0, 0));
                var bounds = new Rect(topLeft, new WpfSize(element.ActualWidth, element.ActualHeight));
                return bounds.Contains(windowPoint);
            }
            catch
            {
                return false;
            }
        }

        private void BuildWaveAnimation()
        {
            _waveAnimation = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            var bars  = new[] { Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7, Bar8 };
            var peaks = new[] { 20.0, 32.0, 44.0, 32.0, 14.0, 38.0, 26.0, 10.0 };

            for (int i = 0; i < bars.Length; i++)
            {
                var bar    = bars[i];
                var peak   = peaks[i];
                var offset = TimeSpan.FromMilliseconds(i * 80);

                var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(6,    KeyTime.FromTimeSpan(offset)));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(offset + TimeSpan.FromMilliseconds(300))));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(6,    KeyTime.FromTimeSpan(offset + TimeSpan.FromMilliseconds(600))));

                Storyboard.SetTarget(anim, bar);
                Storyboard.SetTargetProperty(anim, new PropertyPath("Height"));
                _waveAnimation.Children.Add(anim);
            }
        }

        public new void Show()
        {
            if (!_hasCustomPosition) PositionAtBottomCenter();
            SetState(OverlayState.Idle);
            base.Show();
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, fade);
        }

        public new void Hide()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) => { base.Hide(); Opacity = 1; };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
