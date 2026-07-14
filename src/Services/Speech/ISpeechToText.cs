using System.Threading.Tasks;

namespace Lumi.Services.Speech
{
    public interface ISpeechToText
    {
        string ProviderName { get; }
        Task<string> TranscribeAsync(byte[] wavData, string language = "de");
    }

    public interface ISmoothingService
    {
        Task<string> SmoothAsync(string rawTranscript);
    }

    public interface ITextToSpeech
    {
        Task SpeakAsync(string text);
    }
}
