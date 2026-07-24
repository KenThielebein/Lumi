using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lumi.Services.Diagnostics
{
    /// <summary>
    /// Datenschutzsparsame technische Laufzeitdiagnose. Es werden ausschließlich
    /// Stufen, Dauer-, Größen- und Fehler-Metadaten geschrieben – niemals Audio,
    /// Transkript, Prompt, Clipboard-Inhalt oder Zugangsdaten.
    /// </summary>
    public static class LumiDiagnostics
    {
        private static readonly object FileSync = new();
        private static volatile bool _enabled;

        public static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumi", "logs");

        public static void Configure(bool enabled) => _enabled = enabled;

        public static void Write(
            string eventName,
            params (string Key, object? Value)[] properties)
        {
            if (!_enabled) return;

            var values = properties.Select(property =>
                $"{Sanitize(property.Key)}={Sanitize(property.Value)}");
            var line = $"{DateTimeOffset.Now:O}\tevent={Sanitize(eventName)}\t" +
                       string.Join("\t", values) + Environment.NewLine;
            var path = Path.Combine(LogDirectory, $"lumi-{DateTime.Now:yyyy-MM-dd}.log");

            _ = Task.Run(() =>
            {
                try
                {
                    lock (FileSync)
                    {
                        Directory.CreateDirectory(LogDirectory);
                        File.AppendAllText(path, line);
                    }
                }
                catch
                {
                    // Diagnose darf die eigentliche Diktierfunktion nie stören.
                }
            });
        }

        private static string Sanitize(object? value)
        {
            var text = value?.ToString() ?? "";
            text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return text.Length <= 160 ? text : text[..160];
        }
    }
}
