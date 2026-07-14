using System.Threading.Tasks;

namespace Lumi.Services.Speech
{
    public class NullSttClient : ISpeechToText
    {
        public string ProviderName => "None";
        public Task<string> TranscribeAsync(byte[] wavData, string language = "de")
            => Task.FromResult("⚠ Kein API-Key konfiguriert");
    }
}
