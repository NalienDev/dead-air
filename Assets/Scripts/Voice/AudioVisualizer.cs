// Base bar visualizer from https://rumpledcode.com/, extended with live microphone input.
using System;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using UnityEngine;

/// <summary>
/// Spectrum bar visualizer driven by either an AudioSource clip or the local player's Dissonance mic.
/// </summary>
// Uses Dissonance's already-open mic stream rather than calling Microphone.Start, which
// would steal the recording session Dissonance is already using.
public class AudioVisualizer : BaseMicrophoneSubscriber
{
    [Header("References")]
    [Tooltip("Used only when Use Microphone is off.")]
    public AudioSource audioSource;
    public Transform[] bars;

    [Header("Microphone")]
    [Tooltip("If enabled, bars react to the local player's voice instead of audioSource.")]
    public bool useMicrophone = true;
    [Tooltip("Multiplier on raw mic FFT magnitudes before Amplification.")]
    public float micGain = 60f;

    [Header("Settings")]
    public FrequencyFocusWindow frequencyFocusWindow = FrequencyFocusWindow.FirstQuarter;
    public float amplification = 1.0f;
    public float baseHeight = 0.0f;
    [Tooltip("Used only when Use Microphone is off.")]
    public FFTWindow fftWindow = FFTWindow.BlackmanHarris;
    public bool useDecibels;

    [Header("State")]
    public float[] spectrumData;
    [Tooltip("Debug: true once Dissonance is delivering mic data.")]
    public bool micStreamActive;
    [Tooltip("Debug: average of the current mic spectrum, after micGain.")]
    public float micLevelDebug;

    private const int FftSize = 1024; // power of 2

    private DissonanceComms _dissonanceComms;
    private float[] _ring = new float[FftSize];
    private int _writePos;
    private float[] _fftReal = new float[FftSize];
    private float[] _fftImag = new float[FftSize];
    private float[] _micSpectrum = new float[FftSize / 2];
    private int _channels = 1;
    private bool _micReady;

    void Awake()
    {
        // Must be a power of 2 between 64 and 8192.
        spectrumData = new float[4096];
    }

    void Start()
    {
        if (!useMicrophone) return;

        _dissonanceComms = FindFirstObjectByType<DissonanceComms>();
        if (_dissonanceComms == null)
        {
            Debug.LogWarning("[AudioVisualizer] No DissonanceComms found; microphone mode disabled.", this);
            useMicrophone = false;
            return;
        }

        _dissonanceComms.SubscribeToRecordedAudio(this);
    }

    private void OnDestroy()
    {
        DissonanceComms comms = _dissonanceComms != null ? _dissonanceComms : FindFirstObjectByType<DissonanceComms>();
        comms?.UnsubscribeFromRecordedAudio(this);
    }

    protected override void ResetAudioStream(WaveFormat waveFormat)
    {
        _channels = Mathf.Max(1, waveFormat.Channels);
        Array.Clear(_ring, 0, _ring.Length);
        _writePos = 0;
        _micReady = true;
    }

    protected override void ProcessAudio(ArraySegment<float> data)
    {
        if (_dissonanceComms != null && _dissonanceComms.IsMuted)
            return;

        int frames = data.Count / _channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            int baseIdx = data.Offset + f * _channels;
            for (int c = 0; c < _channels; c++)
                sum += data.Array![baseIdx + c];

            _ring[_writePos] = sum / _channels;
            _writePos = (_writePos + 1) % FftSize;
        }
    }

    public override void Update()
    {
        base.Update(); // pumps Dissonance's audio buffer into ProcessAudio

        if (bars == null || bars.Length == 0) return;

        if (useMicrophone)
        {
            micStreamActive = _micReady;
            if (!_micReady) return;
            ComputeMicSpectrum();

            float sum = 0f;
            for (int i = 0; i < _micSpectrum.Length; i++) sum += _micSpectrum[i];
            micLevelDebug = sum / _micSpectrum.Length;

            ApplyBarsFromSpectrum(_micSpectrum);
        }
        else
        {
            if (audioSource == null) return;
            audioSource.GetSpectrumData(spectrumData, 0, fftWindow);
            ApplyBarsFromSpectrum(spectrumData);
        }
    }

    private void ApplyBarsFromSpectrum(float[] spectrum)
    {
        int blockSize = Mathf.Max(1, spectrum.Length / bars.Length / (int)frequencyFocusWindow);

        for (int i = 0; i < bars.Length; i++)
        {
            if (bars[i] == null) continue;

            float sum = 0f;
            int baseIdx = i * blockSize;
            int count = 0;
            for (int j = 0; j < blockSize; j++)
            {
                int idx = baseIdx + j;
                if (idx >= spectrum.Length) break;
                sum += spectrum[idx];
                count++;
            }
            if (count > 0) sum /= count;

            float amplitude = Mathf.Clamp(sum, 1e-7f, 1f);
            Vector3 scale = bars[i].localScale;
            scale.y = useDecibels
                ? -Mathf.Log10(amplitude) * amplification / 200f
                : sum * amplification + baseHeight;
            bars[i].localScale = scale;
        }
    }

    private void ComputeMicSpectrum()
    {
        for (int i = 0; i < FftSize; i++)
        {
            float sample = _ring[(_writePos + i) % FftSize];
            // Hann window to reduce spectral leakage.
            float w = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (FftSize - 1));
            _fftReal[i] = sample * w;
            _fftImag[i] = 0f;
        }

        FFT(_fftReal, _fftImag);

        for (int i = 0; i < _micSpectrum.Length; i++)
        {
            float re = _fftReal[i];
            float im = _fftImag[i];
            _micSpectrum[i] = (Mathf.Sqrt(re * re + im * im) / FftSize) * micGain;
        }
    }

    // In-place iterative radix-2 Cooley-Tukey FFT. real.Length must be a power of two.
    private static void FFT(float[] real, float[] imag)
    {
        int n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            float wr = (float)Math.Cos(angle);
            float wi = (float)Math.Sin(angle);

            for (int i = 0; i < n; i += len)
            {
                float curWr = 1f, curWi = 0f;
                int half = len / 2;
                for (int j = 0; j < half; j++)
                {
                    float ur = real[i + j];
                    float ui = imag[i + j];
                    float vr = real[i + j + half] * curWr - imag[i + j + half] * curWi;
                    float vi = real[i + j + half] * curWi + imag[i + j + half] * curWr;

                    real[i + j] = ur + vr;
                    imag[i + j] = ui + vi;
                    real[i + j + half] = ur - vr;
                    imag[i + j + half] = ui - vi;

                    float nextWr = curWr * wr - curWi * wi;
                    float nextWi = curWr * wi + curWi * wr;
                    curWr = nextWr;
                    curWi = nextWi;
                }
            }
        }
    }
}

public enum FrequencyFocusWindow
{
    Entire = 1,
    FirstHalf = 2,
    FirstQuarter = 4,
    FirstEight = 8,
    FirstSixteenth = 16
}
