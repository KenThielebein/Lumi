using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Lumi.Services.AI;

namespace Lumi.UI
{
    public partial class ModelPickerWindow : Window
    {
        private ICollectionView? _view;

        /// <summary>Die gewählte Modell-ID – nach ShowDialog() verfügbar.</summary>
        public string SelectedModelId { get; private set; }

        public ModelPickerWindow(string currentModelId)
        {
            InitializeComponent();
            SelectedModelId = currentModelId;
            Loaded += async (_, _) => await LoadModelsAsync();
        }

        // ── Laden ──────────────────────────────────────────────────────────────

        private async Task LoadModelsAsync(bool forceRefresh = false)
        {
            ModelsList.Visibility = Visibility.Collapsed;
            StatusText.Text       = "Modelle werden von OpenRouter geladen…";
            StatusText.Visibility = Visibility.Visible;
            SelectBtn.IsEnabled   = false;

            try
            {
                var models = await OpenRouterModelFetcher.GetModelsAsync(forceRefresh);

                _view = CollectionViewSource.GetDefaultView(models);
                _view.Filter = FilterModel;
                ModelsList.ItemsSource = _view;

                // Aktuelles Modell vorselektieren und scrollen
                foreach (var m in models)
                {
                    if (m.Id == SelectedModelId)
                    {
                        ModelsList.SelectedItem = m;
                        ModelsList.ScrollIntoView(m);
                        UpdateSelectedPanel(m);
                        break;
                    }
                }

                StatusText.Visibility = Visibility.Collapsed;
                ModelsList.Visibility = Visibility.Visible;
                SearchBox.Focus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"⚠  Fehler beim Laden: {ex.Message}";
            }
        }

        // ── Filter ─────────────────────────────────────────────────────────────

        private bool FilterModel(object item)
        {
            if (item is not OpenRouterModel m) return false;
            var q = SearchBox.Text.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            return m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                   m.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchBox_TextChanged(object sender,
            System.Windows.Controls.TextChangedEventArgs e)
        {
            Placeholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            _view?.Refresh();
        }

        // ── Auswahl ────────────────────────────────────────────────────────────

        private void ModelsList_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ModelsList.SelectedItem is OpenRouterModel m)
            {
                SelectedModelId = m.Id;
                UpdateSelectedPanel(m);
                SelectBtn.IsEnabled = true;
            }
        }

        private void ModelsList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectBtn.IsEnabled) Confirm();
        }

        private void UpdateSelectedPanel(OpenRouterModel m)
        {
            SelectedName.Text  = $"{m.Name}  –  {m.Id}";
            SelectedPrice.Text = m.PriceDisplay;
            SelectedPanel.Visibility = Visibility.Visible;
        }

        // ── Button-Handler ─────────────────────────────────────────────────────

        private async void Reload_Click(object sender, RoutedEventArgs e)
            => await LoadModelsAsync(forceRefresh: true);

        private void Select_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Confirm()
        {
            DialogResult = true;
            Close();
        }
    }
}
