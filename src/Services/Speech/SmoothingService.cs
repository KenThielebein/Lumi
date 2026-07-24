using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Lumi.Services.AI;

namespace Lumi.Services.Speech
{
    public class SmoothingService : ISmoothingService, IDisposable
    {
        private readonly IAIProvider _ai;

        private const string SystemPrompt =
            "Du erhältst einen automatisch transkribierten Text. " +
            "Entferne Versprecher und Wiederholungen, setze Satzzeichen korrekt und glätte den Text natürlich. " +
            "Ändere nichts am Inhalt, Stil oder an Fachbegriffen. " +
            "Antworte ausschließlich mit dem korrigierten Text, ohne Erklärung oder Kommentar.";

        public SmoothingService(IAIProvider ai)
        {
            _ai = ai;
        }

        public async Task<string> SmoothAsync(string rawTranscript)
        {
            if (string.IsNullOrWhiteSpace(rawTranscript)) return rawTranscript;
            try
            {
                return await _ai.CompleteAsync(rawTranscript, SystemPrompt);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lumi] Smoothing failed, using raw transcript: {ex.Message}");
                return rawTranscript;
            }
        }

        public void Dispose()
        {
            if (_ai is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
