using System.Threading.Tasks;

namespace Lumi.Services.Speech
{
    public class PassThroughSmoother : ISmoothingService
    {
        public Task<string> SmoothAsync(string rawTranscript) => Task.FromResult(rawTranscript);
    }
}
