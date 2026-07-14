using System;
using System.Threading.Tasks;

namespace Lumi.Services.Audio
{
    public interface IAudioService
    {
        bool IsRecording { get; }
        event EventHandler<float> VolumeChanged;
        Task StartRecordingAsync();
        Task<byte[]> StopRecordingAsync();
    }
}
