using System;
using System.Runtime.InteropServices;

namespace Harmony
{
    /// <summary>
    /// Continuous tone player built directly on the Windows multimedia waveOut API
    /// (winmm.dll). No external dependencies - compiles into a single small exe on
    /// .NET Framework 4.8. Streams 16-bit stereo PCM generated from sine layers.
    /// </summary>
    public sealed class ToneGenerator : IDisposable
    {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        private const int BitsPerSample = 16;
        private const int BufferCount = 3;
        private const int FramesPerBuffer = 4410; // ~100 ms per buffer

        private IntPtr _hWaveOut = IntPtr.Zero;
        private WaveHeader[] _headers = Array.Empty<WaveHeader>();
        private GCHandle[] _bufferHandles = Array.Empty<GCHandle>();
        private GCHandle[] _headerHandles = Array.Empty<GCHandle>();
        private WaveOutProc _callback = null!;
        private volatile bool _running;

        private readonly object _sync = new object();
        private TonePreset _preset = TonePreset.Schumann;
        private float _volume = 0.15f;

        // Oscillator phases
        private double _phaseLeft, _phaseRight;
        private double[] _layerPhases = Array.Empty<double>();

        public bool IsPlaying => _running;

        public void Start(TonePreset preset, float volume, string? deviceName = null)
        {
            Stop();
            lock (_sync)
            {
                _preset = preset;
                _volume = Clamp(volume, 0f, 1f);
                _phaseLeft = _phaseRight = 0;
                _layerPhases = new double[preset.Layers.Length];
            }

            var fmt = new WaveFormatEx
            {
                wFormatTag = 1, // PCM
                nChannels = (ushort)Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = BitsPerSample,
                nBlockAlign = (ushort)(Channels * BitsPerSample / 8),
                cbSize = 0
            };
            fmt.nAvgBytesPerSec = fmt.nSamplesPerSec * fmt.nBlockAlign;

            _callback = WaveOutCallback;
            int mmr = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, _callback, IntPtr.Zero, CALLBACK_FUNCTION);
            if (mmr != 0)
            {
                _hWaveOut = IntPtr.Zero;
                throw new InvalidOperationException("Unable to open audio output device (waveOutOpen error " + mmr + ").");
            }

            int bytesPerBuffer = FramesPerBuffer * Channels * (BitsPerSample / 8);
            _headers = new WaveHeader[BufferCount];
            _bufferHandles = new GCHandle[BufferCount];
            _headerHandles = new GCHandle[BufferCount];

            _running = true;

            for (int i = 0; i < BufferCount; i++)
            {
                var data = new byte[bytesPerBuffer];
                RenderInto(data);
                _bufferHandles[i] = GCHandle.Alloc(data, GCHandleType.Pinned);

                _headers[i] = new WaveHeader
                {
                    lpData = _bufferHandles[i].AddrOfPinnedObject(),
                    dwBufferLength = (uint)bytesPerBuffer,
                    dwFlags = 0
                };
                _headerHandles[i] = GCHandle.Alloc(_headers[i], GCHandleType.Pinned);
                IntPtr pHeader = _headerHandles[i].AddrOfPinnedObject();
                waveOutPrepareHeader(_hWaveOut, pHeader, Marshal.SizeOf(typeof(WaveHeader)));
                waveOutWrite(_hWaveOut, pHeader, Marshal.SizeOf(typeof(WaveHeader)));
            }
        }

        public void SetVolume(float volume)
        {
            lock (_sync) { _volume = Clamp(volume, 0f, 1f); }
        }

        public void SetPreset(TonePreset preset)
        {
            lock (_sync)
            {
                _preset = preset;
                if (_layerPhases.Length != preset.Layers.Length)
                    _layerPhases = new double[preset.Layers.Length];
            }
        }

        public void Stop()
        {
            if (!_running && _hWaveOut == IntPtr.Zero) return;
            _running = false;

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                for (int i = 0; i < _headerHandles.Length; i++)
                {
                    if (_headerHandles[i].IsAllocated)
                    {
                        IntPtr pHeader = _headerHandles[i].AddrOfPinnedObject();
                        waveOutUnprepareHeader(_hWaveOut, pHeader, Marshal.SizeOf(typeof(WaveHeader)));
                        _headerHandles[i].Free();
                    }
                    if (i < _bufferHandles.Length && _bufferHandles[i].IsAllocated)
                        _bufferHandles[i].Free();
                }
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
            _headers = Array.Empty<WaveHeader>();
            _bufferHandles = Array.Empty<GCHandle>();
            _headerHandles = Array.Empty<GCHandle>();
        }

        // Called by winmm on its own thread when a buffer finishes.
        private void WaveOutCallback(IntPtr hWaveOut, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
        {
            if (uMsg != WOM_DONE || !_running || _hWaveOut == IntPtr.Zero) return;

            // Find which header this is, refill and requeue it.
            for (int i = 0; i < _headerHandles.Length; i++)
            {
                if (!_headerHandles[i].IsAllocated) continue;
                IntPtr pHeader = _headerHandles[i].AddrOfPinnedObject();
                if (pHeader != dwParam1) continue;

                try
                {
                    var data = (byte[])_bufferHandles[i].Target!;
                    RenderInto(data);
                    waveOutWrite(_hWaveOut, pHeader, Marshal.SizeOf(typeof(WaveHeader)));
                }
                catch { /* device closing */ }
                break;
            }
        }

        // Generate one buffer's worth of 16-bit stereo PCM into 'data'.
        private void RenderInto(byte[] data)
        {
            TonePreset p;
            float vol;
            lock (_sync)
            {
                p = _preset;
                vol = _volume;
                if (_layerPhases.Length != p.Layers.Length)
                    _layerPhases = new double[p.Layers.Length];
            }

            int frames = data.Length / (Channels * (BitsPerSample / 8));
            int idx = 0;
            for (int i = 0; i < frames; i++)
            {
                float left = 0f, right = 0f;

                if (p.FrequencyLeft > 0)
                {
                    left += (float)(Math.Sin(_phaseLeft) * p.PrimaryAmplitude);
                    _phaseLeft += 2 * Math.PI * p.FrequencyLeft / SampleRate;
                    if (_phaseLeft > 2 * Math.PI) _phaseLeft -= 2 * Math.PI;
                }

                if (p.FrequencyRight > 0)
                {
                    right += (float)(Math.Sin(_phaseRight) * p.PrimaryAmplitude);
                    _phaseRight += 2 * Math.PI * p.FrequencyRight / SampleRate;
                    if (_phaseRight > 2 * Math.PI) _phaseRight -= 2 * Math.PI;
                }
                else
                {
                    right = left;
                }

                for (int l = 0; l < p.Layers.Length; l++)
                {
                    var layer = p.Layers[l];
                    float freq = layer.Frequency;
                    float amp = layer.Amplitude;
                    if (freq <= 0) continue;
                    float sample = (float)(Math.Sin(_layerPhases[l]) * amp);
                    _layerPhases[l] += 2 * Math.PI * freq / SampleRate;
                    if (_layerPhases[l] > 2 * Math.PI) _layerPhases[l] -= 2 * Math.PI;
                    left += sample;
                    right += sample;
                }

                short sL = FloatToPcm16(left * vol);
                short sR = FloatToPcm16(right * vol);

                data[idx++] = (byte)(sL & 0xFF);
                data[idx++] = (byte)((sL >> 8) & 0xFF);
                data[idx++] = (byte)(sR & 0xFF);
                data[idx++] = (byte)((sR >> 8) & 0xFF);
            }
        }

        private static short FloatToPcm16(float value)
        {
            if (value > 1f) value = 1f;
            else if (value < -1f) value = -1f;
            return (short)(value * short.MaxValue);
        }

        private static float Clamp(float v, float min, float max)
            => v < min ? min : (v > max ? max : v);

        public void Dispose() => Stop();

        // ===== winmm.dll interop =====
        private const int WAVE_MAPPER = -1;
        private const int CALLBACK_FUNCTION = 0x00030000;
        private const uint WOM_DONE = 0x3BD;

        private delegate void WaveOutProc(IntPtr hWaveOut, uint uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WaveFormatEx lpFormat, WaveOutProc dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);
    }

    public sealed class TonePreset
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public float FrequencyLeft { get; set; }
        public float FrequencyRight { get; set; }
        public float PrimaryAmplitude { get; set; } = 0.5f;
        public LayerTone[] Layers { get; set; } = Array.Empty<LayerTone>();
        public float BinauralBeatHz => FrequencyRight > 0 ? Math.Abs(FrequencyRight - FrequencyLeft) : 0;

        private static LayerTone[] L(params (float f, float a)[] items)
        {
            var arr = new LayerTone[items.Length];
            for (int i = 0; i < items.Length; i++) arr[i] = new LayerTone(items[i].f, items[i].a);
            return arr;
        }

        public static TonePreset AllCombined => new TonePreset
        {
            Name = "All Healing Frequencies",
            Description = "Schumann binaural + all 9 solfeggio frequencies layered together.",
            FrequencyLeft = 200f,
            FrequencyRight = 207.83f,
            PrimaryAmplitude = 0.15f,
            Layers = L((174f, 0.10f), (285f, 0.10f), (396f, 0.10f),
                       (432f, 0.10f), (528f, 0.12f), (639f, 0.08f),
                       (741f, 0.08f), (852f, 0.08f), (963f, 0.08f))
        };

        public static TonePreset Schumann => new TonePreset
        {
            Name = "Schumann Resonance",
            Description = "7.83 Hz - Earth's natural frequency. Grounding, calm, stress relief.",
            FrequencyLeft = 200f, FrequencyRight = 207.83f, PrimaryAmplitude = 0.5f
        };

        public static TonePreset DeepRelax => new TonePreset
        {
            Name = "Deep Relaxation",
            Description = "4 Hz theta binaural - deep meditation, near-sleep state.",
            FrequencyLeft = 150f, FrequencyRight = 154f, PrimaryAmplitude = 0.5f
        };

        public static TonePreset Focus => new TonePreset
        {
            Name = "Focus & Concentration",
            Description = "14 Hz beta binaural - alertness, productivity.",
            FrequencyLeft = 200f, FrequencyRight = 214f, PrimaryAmplitude = 0.4f
        };

        public static TonePreset Healing528 => new TonePreset
        {
            Name = "528 Hz - Miracle Tone",
            Description = "DNA repair, transformation. With 264 Hz sub-harmonic.",
            FrequencyLeft = 528f, PrimaryAmplitude = 0.5f,
            Layers = L((264f, 0.15f))
        };

        public static TonePreset Healing432 => new TonePreset
        {
            Name = "432 Hz - Natural Tuning",
            Description = "Verdi's A - harmonious, calming alternative to 440 Hz.",
            FrequencyLeft = 432f, PrimaryAmplitude = 0.5f,
            Layers = L((216f, 0.15f))
        };

        public static TonePreset Sleep => new TonePreset
        {
            Name = "Sleep Induction",
            Description = "2 Hz delta binaural - deep dreamless sleep.",
            FrequencyLeft = 100f, FrequencyRight = 102f, PrimaryAmplitude = 0.4f
        };

        public static TonePreset AntiAnxiety => new TonePreset
        {
            Name = "Anti-Anxiety",
            Description = "10 Hz alpha binaural + 396 Hz solfeggio (liberation from fear).",
            FrequencyLeft = 198f, FrequencyRight = 208f, PrimaryAmplitude = 0.4f,
            Layers = L((396f, 0.2f))
        };

        public static TonePreset BodyHeal => new TonePreset
        {
            Name = "Body Healing",
            Description = "174 Hz (pain) + 285 Hz (tissue) + Schumann binaural.",
            FrequencyLeft = 174f, FrequencyRight = 181.83f, PrimaryAmplitude = 0.35f,
            Layers = L((285f, 0.3f))
        };

        public static TonePreset[] AllPresets => new[]
        {
            AllCombined, Schumann, DeepRelax, Focus, Healing528, Healing432, Sleep, AntiAnxiety, BodyHeal
        };
    }

    public struct LayerTone
    {
        public float Frequency;
        public float Amplitude;
        public LayerTone(float frequency, float amplitude) { Frequency = frequency; Amplitude = amplitude; }
    }
}
