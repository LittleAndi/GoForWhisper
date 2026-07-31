using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Services;

/// <summary>
/// Decoded audio in the shape Whisper wants: 16 kHz mono float samples.
/// </summary>
/// <param name="Samples">Normalised mono samples at 16 kHz.</param>
/// <param name="Offset">
/// How much leading silence was trimmed. Added back to segment timestamps so
/// they still refer to positions in the original file.
/// </param>
public readonly record struct PreparedAudio(ReadOnlyMemory<float> Samples, TimeSpan Offset);

public static class AudioPreprocessor
{
    private const int TargetSampleRate = 16000;

    /// <summary>Windows quieter than this count as silence (-50 dBFS).</summary>
    private const float SilenceFloor = 0.00316f;

    private const int SilenceWindowSamples = TargetSampleRate / 50; // 20 ms
    private const int SilencePaddingSamples = TargetSampleRate / 5; // 200 ms

    /// <summary>
    /// Decodes any format NAudio can open (wav/mp3/aiff natively, m4a/flac/wma
    /// via Media Foundation), downmixes to mono, resamples to 16 kHz, optionally
    /// trims leading and trailing silence, and normalises level.
    /// </summary>
    public static PreparedAudio Prepare(string file, LocalWhisperOptions options)
    {
        using var reader = new AudioFileReader(file);

        ISampleProvider source = reader;
        if (source.WaveFormat.Channels > 1)
        {
            source = new MonoMixSampleProvider(source);
        }

        if (source.WaveFormat.SampleRate != TargetSampleRate)
        {
            source = new WdlResamplingSampleProvider(source, TargetSampleRate);
        }

        var samples = ReadAll(source);

        var (start, end) = options.TrimSilence
            ? FindSpeechBounds(samples)
            : (0, samples.Count);

        var span = CollectionsMarshal.AsSpan(samples)[start..end];
        Normalize(span, options.TargetRmsDbfs, options.PeakCeiling);

        return new PreparedAudio(
            span.ToArray(),
            TimeSpan.FromSeconds((double)start / TargetSampleRate));
    }

    private static List<float> ReadAll(ISampleProvider source)
    {
        var samples = new List<float>();
        var buffer = new float[TargetSampleRate]; // 1 s at a time

        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return samples;
    }

    /// <summary>
    /// Returns the half-open sample range holding speech, padded so a word onset
    /// is never clipped. Falls back to the whole buffer if everything is quiet.
    /// </summary>
    private static (int Start, int End) FindSpeechBounds(List<float> samples)
    {
        var span = CollectionsMarshal.AsSpan(samples);
        var first = -1;
        var last = -1;

        for (var offset = 0; offset < span.Length; offset += SilenceWindowSamples)
        {
            var window = span[offset..Math.Min(offset + SilenceWindowSamples, span.Length)];
            if (Rms(window) <= SilenceFloor)
            {
                continue;
            }

            if (first < 0)
            {
                first = offset;
            }

            last = offset + window.Length;
        }

        if (first < 0)
        {
            return (0, span.Length);
        }

        return (
            Math.Max(0, first - SilencePaddingSamples),
            Math.Min(span.Length, last + SilencePaddingSamples));
    }

    /// <summary>
    /// Brings quiet recordings up to a usable level — Whisper degrades badly on
    /// low-amplitude input. Gain is capped so a loud transient cannot clip.
    /// </summary>
    private static void Normalize(Span<float> samples, float targetRmsDbfs, float peakCeiling)
    {
        var rms = Rms(samples);
        if (rms <= float.Epsilon)
        {
            return;
        }

        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var gain = MathF.Pow(10f, targetRmsDbfs / 20f) / rms;
        if (peak > 0f)
        {
            gain = Math.Min(gain, peakCeiling / peak);
        }

        if (Math.Abs(gain - 1f) < 0.01f)
        {
            return;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
    }

    private static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0f;
        }

        var sum = 0d;
        foreach (var sample in samples)
        {
            sum += (double)sample * sample;
        }

        return (float)Math.Sqrt(sum / samples.Length);
    }
}

/// <summary>
/// Averages an arbitrary number of channels down to one. NAudio's
/// <see cref="StereoToMonoSampleProvider"/> only handles exactly two.
/// </summary>
internal sealed class MonoMixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int channels;
    private float[] buffer = [];

    public MonoMixSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] destination, int offset, int count)
    {
        var wanted = count * channels;
        if (buffer.Length < wanted)
        {
            buffer = new float[wanted];
        }

        var read = source.Read(buffer, 0, wanted);
        var frames = read / channels;

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += buffer[(frame * channels) + channel];
            }

            destination[offset + frame] = sum / channels;
        }

        return frames;
    }
}
