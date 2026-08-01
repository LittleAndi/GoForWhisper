using SherpaOnnx;

namespace Services;

/// <summary>A stretch of audio attributed to one speaker.</summary>
public readonly record struct SpeakerTurn(TimeSpan Start, TimeSpan End, int Speaker);

/// <summary>
/// Splits audio by speaker. Whisper transcribes but has no notion of who is
/// talking, so this runs as a separate pass over the same samples: pyannote
/// segmentation finds the turns, an embedding model characterises each voice,
/// and the embeddings are clustered into speakers.
/// </summary>
public sealed class SpeakerDiarizer(IOptions<DiarizationOptions> options)
{
    private const int ExpectedSampleRate = 16000;

    private readonly DiarizationOptions options = options.Value;

    /// <summary>
    /// Labels <paramref name="audio"/> by speaker. Returned times include the
    /// audio's trim offset, so they line up with Whisper's segment times.
    /// </summary>
    public async Task<IReadOnlyList<SpeakerTurn>> DiarizeAsync(
        PreparedAudio audio,
        int speakers,
        CancellationToken cancellationToken = default)
    {
        var config = await BuildConfigAsync(speakers, cancellationToken);

        using var diarization = new OfflineSpeakerDiarization(config);

        if (diarization.SampleRate != ExpectedSampleRate)
        {
            throw new InvalidOperationException(
                $"Diarization model expects {diarization.SampleRate} Hz but the audio pipeline produces {ExpectedSampleRate} Hz.");
        }

        Console.Error.WriteLine("Diarizing ...");

        // Process is a long, blocking native call, so it goes on the thread pool
        // rather than stalling the caller. It cannot be interrupted once started;
        // the token is checked before it begins, not during.
        var samples = audio.Samples.ToArray();
        var segments = await Task.Run(
            () => diarization.ProcessWithCallback(samples, ReportProgress, IntPtr.Zero),
            cancellationToken);

        Console.Error.WriteLine();

        var turns = new List<SpeakerTurn>(segments.Length);
        foreach (var segment in segments)
        {
            turns.Add(new SpeakerTurn(
                TimeSpan.FromSeconds(segment.Start) + audio.Offset,
                TimeSpan.FromSeconds(segment.End) + audio.Offset,
                segment.Speaker));
        }

        var distinct = turns.Select(turn => turn.Speaker).Distinct().Count();
        Console.Error.WriteLine($"Found {distinct} speakers across {turns.Count} turns.");

        return turns;
    }

    /// <summary>
    /// The native side calls back per chunk — thousands of times over a long
    /// recording — so only whole-percent changes are echoed.
    /// </summary>
    private int lastPercent = -1;

    private int ReportProgress(int processed, int total, IntPtr _)
    {
        if (total <= 0)
        {
            return 0;
        }

        var percent = processed * 100 / total;
        if (percent != lastPercent)
        {
            lastPercent = percent;
            Console.Error.Write($"\r  {percent,3}%");
        }

        return 0;
    }

    private async Task<OfflineSpeakerDiarizationConfig> BuildConfigAsync(
        int speakers,
        CancellationToken cancellationToken)
    {
        var segmentation = await ModelDownloader.EnsureAsync(
            options.SegmentationModelPath,
            options.SegmentationModelUrl,
            cancellationToken);

        var embedding = await ModelDownloader.EnsureAsync(
            options.EmbeddingModelPath,
            options.EmbeddingModelUrl,
            cancellationToken);

        var config = new OfflineSpeakerDiarizationConfig();

        config.Segmentation.Pyannote.Model = segmentation;
        config.Segmentation.NumThreads = options.Threads;
        config.Segmentation.Provider = options.Provider;
        config.Segmentation.Debug = options.Debug ? 1 : 0;

        config.Embedding.Model = embedding;
        config.Embedding.NumThreads = options.Threads;
        config.Embedding.Provider = options.Provider;
        config.Embedding.Debug = options.Debug ? 1 : 0;

        // NumClusters wins when positive; Threshold is only consulted when the
        // count is left open, so the two are deliberately exclusive here.
        var requested = speakers > 0 ? speakers : options.Speakers;
        if (requested > 0)
        {
            config.Clustering.NumClusters = requested;
        }
        else
        {
            config.Clustering.NumClusters = -1;
            config.Clustering.Threshold = options.Threshold;
        }

        config.MinDurationOn = options.MinDurationOn;
        config.MinDurationOff = options.MinDurationOff;

        return config;
    }
}
