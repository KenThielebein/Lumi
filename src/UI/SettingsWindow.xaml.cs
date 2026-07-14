using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Lumi.Config;
using Lumi.Services.AI;
using Lumi.Services.Memory;

namespace Lumi.UI
{
    public partial class SettingsWindow : Window
    {
        private bool _groqRevealed;
        private bool _orRevealed;
        private bool _githubConnected;
        private bool _syncingModelFields;
        private List<string> _customOpenRouterModels = new();
        private HashSet<string> _hiddenOpenRouterModels = new(StringComparer.OrdinalIgnoreCase);

        public SettingsWindow()
        {
            InitializeComponent();
            BuildModelDropdowns();
            LoadConfig();
            _ = LoadVocabularyAsync();
            _ = RefreshGitHubConnectionStatusAsync();
        }

        // ── ComboBox-Aufbau mit gruppierten PopularModels ─────────────────

        private void BuildModelDropdowns()
        {
            var cfg = ConfigManager.Load();
            _customOpenRouterModels = NormalizeModelIds(cfg.CustomOpenRouterModels).ToList();
            _hiddenOpenRouterModels = NormalizeModelIds(cfg.HiddenOpenRouterModels)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            RefreshModelDropdowns();
        }

        private void RefreshModelDropdowns()
        {
            LlmModelCombo.ItemsSource      = BuildGroupedView();
            SmoothingModelCombo.ItemsSource = BuildGroupedView();
        }

        private ListCollectionView BuildGroupedView()
        {
            var view = new ListCollectionView(BuildModelItems());
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PopularModel.Category)));
            return view;
        }

        private List<PopularModel> BuildModelItems()
        {
            var items = PopularModels.All
                .Where(m => !_hiddenOpenRouterModels.Contains(m.Id))
                .ToList();

            var shownIds = items.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var modelId in _customOpenRouterModels.Where(id => !shownIds.Contains(id)))
            {
                items.Add(new PopularModel
                {
                    Id = modelId,
                    DisplayName = CreateCustomModelName(modelId),
                    Category = "Verlauf",
                    IsCustom = true
                });
            }

            return items;
        }

        // ── Konfiguration laden / speichern ───────────────────────────────

        private void LoadConfig()
        {
            var cfg = ConfigManager.Load();

            GroqKeyBox.Password       = cfg.GroqApiKey;
            OpenRouterKeyBox.Password = cfg.OpenRouterApiKey;
            GitHubMemoryTokenBox.Password = cfg.GitHubMemoryToken;

            // Modell-IDs in die TextBoxen (aktive ID) setzen
            LlmModelBox.Text       = cfg.CloudLLMModel;
            SmoothingModelBox.Text = cfg.CloudSmoothingModel;

            // Passendes Element in der Dropdown-Liste vorselektieren (falls vorhanden)
            SyncComboToModelId(LlmModelCombo,       cfg.CloudLLMModel);
            SyncComboToModelId(SmoothingModelCombo, cfg.CloudSmoothingModel);

            OpacitySlider.Value      = cfg.OverlayOpacity * 100;
            SilenceTimeoutSlider.Value = System.Math.Clamp(cfg.SilenceTimeoutMs, 500, 6000);
            SmoothingCheck.IsChecked = cfg.EnableSmoothing;
            AutoStartCheck.IsChecked = cfg.AutoStart;
            GitHubMemorySyncCheck.IsChecked = cfg.GitHubMemorySyncEnabled;
            GitHubMemoryOwnerBox.Text = cfg.GitHubMemoryOwner;
            GitHubMemoryRepoBox.Text = cfg.GitHubMemoryRepo;
            GitHubMemoryBranchBox.Text = string.IsNullOrWhiteSpace(cfg.GitHubMemoryBranch) ? "main" : cfg.GitHubMemoryBranch;
            GitHubMemoryPathBox.Text = string.IsNullOrWhiteSpace(cfg.GitHubMemoryPath) ? "lumi-memory.json" : cfg.GitHubMemoryPath;

            ThemeDark.IsChecked  = cfg.OverlayTheme == "Dark";
            ThemeLight.IsChecked = cfg.OverlayTheme == "Light";
            ThemeAmber.IsChecked = cfg.OverlayTheme == "Amber";
            ThemeBlue.IsChecked  = cfg.OverlayTheme == "Blue";

            if (!ThemeDark.IsChecked.GetValueOrDefault()  &&
                !ThemeLight.IsChecked.GetValueOrDefault() &&
                !ThemeAmber.IsChecked.GetValueOrDefault() &&
                !ThemeBlue.IsChecked.GetValueOrDefault())
                ThemeDark.IsChecked = true;

            UpdateGitHubConnectionUi(false, "Nicht getestet");
        }

        // ── API-Key Reveal-Toggle ─────────────────────────────────────────

        private void ToggleGroqKey(object sender, RoutedEventArgs e)
        {
            _groqRevealed = !_groqRevealed;
            if (_groqRevealed)
            {
                GroqKeyReveal.Text       = GroqKeyBox.Password;
                GroqKeyBox.Visibility    = Visibility.Collapsed;
                GroqKeyReveal.Visibility = Visibility.Visible;
            }
            else
            {
                GroqKeyBox.Password      = GroqKeyReveal.Text;
                GroqKeyReveal.Visibility = Visibility.Collapsed;
                GroqKeyBox.Visibility    = Visibility.Visible;
            }
        }

        private void ToggleOpenRouterKey(object sender, RoutedEventArgs e)
        {
            _orRevealed = !_orRevealed;
            if (_orRevealed)
            {
                OpenRouterKeyReveal.Text       = OpenRouterKeyBox.Password;
                OpenRouterKeyBox.Visibility    = Visibility.Collapsed;
                OpenRouterKeyReveal.Visibility = Visibility.Visible;
            }
            else
            {
                OpenRouterKeyBox.Password      = OpenRouterKeyReveal.Text;
                OpenRouterKeyReveal.Visibility = Visibility.Collapsed;
                OpenRouterKeyBox.Visibility    = Visibility.Visible;
            }
        }

        // ── Modell-Picker (Vollsuche mit Preisen) ─────────────────────────

        // Dropdown-Auswahl → TextBox aktualisieren
        private void LlmModelCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingModelFields) return;

            if (LlmModelCombo.SelectedItem is PopularModel m)
                LlmModelBox.Text = m.Id;
        }

        private void SmoothingModelCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingModelFields) return;

            if (SmoothingModelCombo.SelectedItem is PopularModel m)
                SmoothingModelBox.Text = m.Id;
        }

        private void LlmModelBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_syncingModelFields) return;
            SyncComboToModelId(LlmModelCombo, LlmModelBox.Text.Trim());
        }

        private void SmoothingModelBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_syncingModelFields) return;
            SyncComboToModelId(SmoothingModelCombo, SmoothingModelBox.Text.Trim());
        }

        // Vollsuche (alle OpenRouter-Modelle mit Preisen)
        private void PickLlmModel(object sender, RoutedEventArgs e)
        {
            var picker = new ModelPickerWindow(LlmModelBox.Text.Trim()) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                LlmModelBox.Text = picker.SelectedModelId;
                RememberModelId(picker.SelectedModelId);
                RefreshModelDropdowns();
                SyncComboToModelId(LlmModelCombo, picker.SelectedModelId);
            }
        }

        private void PickSmoothingModel(object sender, RoutedEventArgs e)
        {
            var picker = new ModelPickerWindow(SmoothingModelBox.Text.Trim()) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                SmoothingModelBox.Text = picker.SelectedModelId;
                RememberModelId(picker.SelectedModelId);
                RefreshModelDropdowns();
                SyncComboToModelId(SmoothingModelCombo, picker.SelectedModelId);
            }
        }

        private void RemoveModel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string modelId) return;

            e.Handled = true;
            _customOpenRouterModels.RemoveAll(id =>
                string.Equals(id, modelId, StringComparison.OrdinalIgnoreCase));

            if (PopularModels.All.Any(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase)))
                _hiddenOpenRouterModels.Add(modelId);

            RefreshModelDropdowns();
            SyncComboToModelId(LlmModelCombo, LlmModelBox.Text.Trim());
            SyncComboToModelId(SmoothingModelCombo, SmoothingModelBox.Text.Trim());

            var cfg = BuildConfigFromFields(rememberCurrentModels: false);
            ConfigManager.Save(cfg);
        }

        // Hilfsfunktion: passendes PopularModel in der ComboBox vorauswählen
        private void SyncComboToModelId(System.Windows.Controls.ComboBox combo, string modelId)
        {
            _syncingModelFields = true;
            try
            {
                combo.SelectedItem = null;
                foreach (PopularModel m in PopularModels.All)
                {
                    if (m.Id == modelId)
                    {
                        combo.SelectedItem = m;
                        combo.Text = m.Id;
                        return;
                    }
                }

                combo.Text = modelId;
            }
            finally
            {
                _syncingModelFields = false;
            }
        }

        private void RememberModelId(string modelId)
        {
            var normalized = modelId.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return;

            _hiddenOpenRouterModels.RemoveWhere(id =>
                string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase));

            var isVisiblePopular = PopularModels.All.Any(m =>
                string.Equals(m.Id, normalized, StringComparison.OrdinalIgnoreCase)) &&
                !_hiddenOpenRouterModels.Contains(normalized);

            if (!isVisiblePopular && !_customOpenRouterModels.Any(id =>
                    string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase)))
                _customOpenRouterModels.Add(normalized);
        }

        private static IEnumerable<string> NormalizeModelIds(IEnumerable<string>? modelIds)
            => (modelIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private static string CreateCustomModelName(string modelId)
        {
            var name = modelId.Split('/').LastOrDefault() ?? modelId;
            name = name.Replace(":free", " free", StringComparison.OrdinalIgnoreCase)
                       .Replace(':', ' ')
                       .Replace('-', ' ');
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
        }

        // ── Speichern / Abbrechen ─────────────────────────────────────────

        private async void Save(object sender, RoutedEventArgs e)
        {
            var cfg = BuildConfigFromFields();

            ConfigManager.Save(cfg);
            AutoStartManager.Apply(cfg.AutoStart);
            var memory = new MemoryService(cfg);
            await memory.InitializeAsync();
            await memory.ReplaceVocabularyAsync(ParseVocabulary());

            DialogResult = true;
            Close();
        }

        private async System.Threading.Tasks.Task LoadVocabularyAsync()
        {
            var memory = new MemoryService(ConfigManager.Load());
            await memory.InitializeAsync();
            VocabularyBox.Text = string.Join(
                Environment.NewLine,
                memory.GetVocabulary()
                    .SelectMany(entry => entry.HeardAs.Select(heard => $"{heard} => {entry.Written}")));
        }

        private IEnumerable<VocabularyEntry> ParseVocabulary()
        {
            var pairs = VocabularyBox.Text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(new[] { "=>" }, 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2 &&
                                !string.IsNullOrWhiteSpace(parts[0]) &&
                                !string.IsNullOrWhiteSpace(parts[1]));

            return pairs
                .GroupBy(parts => parts[1], StringComparer.OrdinalIgnoreCase)
                .Select(group => new VocabularyEntry
                {
                    Written = group.Key,
                    HeardAs = group.Select(parts => parts[0])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList()
                })
                .ToList();
        }

        private void Cancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void ToggleGitHubConnection(object sender, RoutedEventArgs e)
        {
            if (_githubConnected)
            {
                GitHubMemorySyncCheck.IsChecked = false;
                var cfg = BuildConfigFromFields();
                ConfigManager.Save(cfg);
                UpdateGitHubConnectionUi(false, "Getrennt");
                return;
            }

            GitHubMemorySyncCheck.IsChecked = true;
            await RefreshGitHubConnectionStatusAsync(saveOnSuccess: true);
        }

        private async Task RefreshGitHubConnectionStatusAsync(bool saveOnSuccess = false)
        {
            if (GitHubMemorySyncCheck.IsChecked != true)
            {
                UpdateGitHubConnectionUi(false, "GitHub-Sync ist deaktiviert");
                return;
            }

            GitHubConnectionButton.IsEnabled = false;
            GitHubConnectionButton.Content = "Pruefe Verbindung...";
            GitHubConnectionStatusText.Text = "GitHub wird kontaktiert.";

            var cfg = BuildConfigFromFields();
            var result = await new GitHubMemoryClient(cfg).TestConnectionAsync();

            if (result.IsConnected && saveOnSuccess)
                ConfigManager.Save(cfg);

            UpdateGitHubConnectionUi(result.IsConnected, result.Message);
            GitHubConnectionButton.IsEnabled = true;
        }

        private void UpdateGitHubConnectionUi(bool connected, string message)
        {
            _githubConnected = connected;
            GitHubConnectionButton.Content = connected ? "Verbunden - trennen" : "Nicht verbunden - verbinden";
            GitHubConnectionButton.Background = new SolidColorBrush(
                connected
                    ? System.Windows.Media.Color.FromRgb(46, 125, 50)
                    : System.Windows.Media.Color.FromRgb(198, 40, 40));
            GitHubConnectionStatusText.Text = message;
        }

        private AppConfig BuildConfigFromFields(bool rememberCurrentModels = true)
        {
            var cfg = ConfigManager.Load();

            cfg.GroqApiKey          = _groqRevealed ? GroqKeyReveal.Text       : GroqKeyBox.Password;
            cfg.OpenRouterApiKey    = _orRevealed   ? OpenRouterKeyReveal.Text : OpenRouterKeyBox.Password;
            cfg.GitHubMemoryToken   = GitHubMemoryTokenBox.Password;
            cfg.CloudLLMModel       = LlmModelBox.Text.Trim();
            cfg.CloudSmoothingModel = SmoothingModelBox.Text.Trim();
            if (rememberCurrentModels)
            {
                RememberModelId(cfg.CloudLLMModel);
                RememberModelId(cfg.CloudSmoothingModel);
            }
            cfg.CustomOpenRouterModels = NormalizeModelIds(_customOpenRouterModels).ToList();
            cfg.HiddenOpenRouterModels = NormalizeModelIds(_hiddenOpenRouterModels).ToList();
            cfg.OverlayOpacity      = OpacitySlider.Value / 100.0;
            cfg.SilenceTimeoutMs    = (int)System.Math.Round(SilenceTimeoutSlider.Value);
            cfg.EnableSmoothing     = SmoothingCheck.IsChecked == true;
            cfg.AutoStart           = AutoStartCheck.IsChecked == true;
            cfg.GitHubMemorySyncEnabled = GitHubMemorySyncCheck.IsChecked == true;
            cfg.GitHubMemoryOwner   = GitHubMemoryOwnerBox.Text.Trim();
            cfg.GitHubMemoryRepo    = GitHubMemoryRepoBox.Text.Trim();
            cfg.GitHubMemoryBranch  = string.IsNullOrWhiteSpace(GitHubMemoryBranchBox.Text) ? "main" : GitHubMemoryBranchBox.Text.Trim();
            cfg.GitHubMemoryPath    = string.IsNullOrWhiteSpace(GitHubMemoryPathBox.Text) ? "lumi-memory.json" : GitHubMemoryPathBox.Text.Trim();
            cfg.OverlayTheme        = ThemeLight.IsChecked == true ? "Light"
                                    : ThemeAmber.IsChecked == true ? "Amber"
                                    : ThemeBlue.IsChecked  == true ? "Blue"
                                    : "Dark";

            return cfg;
        }

    }
}
