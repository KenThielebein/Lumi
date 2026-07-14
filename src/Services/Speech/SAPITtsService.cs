using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace Lumi.Services.Speech
{
    public class SAPITtsService : ITextToSpeech
    {
        private readonly SpeechSynthesizer _synth = new();

        public SAPITtsService()
        {
            _synth.Rate   = 1;   // -10 bis 10
            _synth.Volume = 100; // 0–100
        }

        public Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
            // SpeakAsync auf Thread-Pool, damit UI nicht blockiert
            return Task.Run(() => _synth.Speak(text));
        }
    }
}
