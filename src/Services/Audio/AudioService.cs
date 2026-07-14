using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Lumi.Services.Audio
{
    public class AudioService : IAudioService, IDisposable
    {
        private readonly object _sync = new();
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private WaveInEvent? _waveIn;
        private MemoryStream? _buffer;
        private WaveFileWriter? _writer;
        private DateTime _lastSound = DateTime.UtcNow;
        private DateTime _captureFrom;
        private CancellationTokenSource? _vadCts;
        private bool _isRecording;

        private const float SilenceThreshold = 0.01f;

        public int SilenceTimeoutMs { get; set; } = 3000;

        public bool IsRecording
        {
            get { lock (_sync) return _isRecording; }
        }

        public event EventHandler<float>? VolumeChanged;
        public event EventHandler? SilenceDetected;

        public async Task StartRecordingAsync()
        {
            await _lifecycleGate.WaitAsync();
            CancellationToken vadToken;
            try
            {
                lock (_sync)
                {
                    if (_isRecording) return;

                    CleanupLocked();
                    _buffer = new MemoryStream();
                    _waveIn = new WaveInEvent
                    {
                        WaveFormat = new WaveFormat(16000, 16, 1),
                        BufferMilliseconds = 50
                    };
                    _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
                    _captureFrom = DateTime.UtcNow.AddMilliseconds(100);
                    _lastSound = _captureFrom;
                    _vadCts = new CancellationTokenSource();
                    vadToken = _vadCts.Token;
                    _waveIn.DataAvailable += OnDataAvailable;
                    _isRecording = true;

                    try
                    {
                        _waveIn.StartRecording();
                    }
                    catch
                    {
                        _isRecording = false;
                        CleanupLocked();
                        throw;
                    }
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }

            _ = WatchSilenceAsync(vadToken);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            float rms;
            lock (_sync)
            {
                if (!_isRecording || _writer == null || DateTime.UtcNow < _captureFrom) return;

                _writer.Write(e.Buffer, 0, e.BytesRecorded);
                rms = ComputeRms(e.Buffer, e.BytesRecorded);
                if (rms > SilenceThreshold)
                    _lastSound = DateTime.UtcNow;
            }

            VolumeChanged?.Invoke(this, rms);
        }

        private async Task WatchSilenceAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(200, ct).ConfigureAwait(false);

                    bool silenceDetected;
                    lock (_sync)
                    {
                        var silenceTimeoutMs = Math.Clamp(SilenceTimeoutMs, 500, 6000);
                        silenceDetected = _isRecording &&
                            (DateTime.UtcNow - _lastSound).TotalMilliseconds > silenceTimeoutMs;
                    }

                    if (silenceDetected)
                    {
                        SilenceDetected?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when recording is stopped manually.
            }
        }

        public async Task<byte[]> StopRecordingAsync()
        {
            await _lifecycleGate.WaitAsync();
            try
            {
                lock (_sync)
                {
                    if (!_isRecording) return Array.Empty<byte>();

                    // Block a concurrent VAD/hotkey stop before cleanup starts.
                    _isRecording = false;
                    _vadCts?.Cancel();
                    _waveIn?.StopRecording();
                }

                await Task.Delay(100);

                lock (_sync)
                {
                    // Dispose writes the final WAV header lengths before ToArray().
                    _writer?.Dispose();
                    _writer = null;

                    var data = _buffer?.ToArray() ?? Array.Empty<byte>();
                    CleanupLocked();
                    return data;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        private void CleanupLocked()
        {
            if (_waveIn != null)
                _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn?.Dispose();
            _waveIn = null;
            _writer?.Dispose();
            _writer = null;
            _buffer?.Dispose();
            _buffer = null;
            _vadCts?.Dispose();
            _vadCts = null;
        }

        private static float ComputeRms(byte[] buffer, int length)
        {
            double sum = 0;
            int samples = length / 2;
            for (int i = 0; i < length - 1; i += 2)
            {
                short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                sum += (double)s * s;
            }

            return samples > 0 ? (float)Math.Sqrt(sum / samples) / 32768f : 0f;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _isRecording = false;
                _vadCts?.Cancel();
                CleanupLocked();
            }
        }
    }
}
