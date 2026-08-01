using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Services;

public interface IWhisperService
{
    Task SpeechToText(
        string file,
        string language,
        TranscriptOptions output,
        CancellationToken cancellationToken = default);
}

public class WhisperService(IOptions<OpenAIOptions> options) : IWhisperService
{
    private readonly OpenAIOptions options = options.Value;

    public async Task SpeechToText(
        string file,
        string language,
        TranscriptOptions output,
        CancellationToken cancellationToken = default)
    {
        OpenAIClient openAIClient = new(options.ApiKey);

        var client = openAIClient.GetAudioClient(options.Model);
        var result = await client.TranscribeAudioAsync(
            file,
            options: new OpenAI.Audio.AudioTranscriptionOptions
            {
                // The API wants an ISO-639-1 code; null means auto-detect.
                // "auto" is a whisper.cpp convention and is not accepted here.
                Language = NormalizeLanguage(language),
                Temperature = 0f,
            });

        if (output.Diarize)
        {
            Console.Error.WriteLine(
                "Warning: --diarize is not available on the OpenAI path; use LocalSpeechToText.");
        }

        using var sink = TranscriptSink.Create(output.OutputPath);

        // This path returns one flat string, so there are no segment times to
        // print even when --timestamps is asked for.
        sink.Write(result.Value.Text, TimeSpan.Zero);
    }

    private static string? NormalizeLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? null
            : language;
}

public class LocalWhisperService(
    IOptions<LocalWhisperOptions> options,
    SpeakerDiarizer diarizer) : IWhisperService
{
    private readonly LocalWhisperOptions options = options.Value;

    public async Task SpeechToText(
        string file,
        string language,
        TranscriptOptions output,
        CancellationToken cancellationToken = default)
    {
        if (options.Debug)
        {
            // Routed to stderr so diagnostics never contaminate the transcript.
            LogProvider.AddLogger((level, message) =>
                Console.Error.WriteLine($"[{level}] {message?.TrimEnd()}"));
        }

        var modelPath = await WhisperModelProvider.EnsureModelAsync(options, cancellationToken);

        var audio = AudioPreprocessor.Prepare(file, options);

        // Diarization runs first: it is a separate pass over the same samples,
        // and knowing the turns up front means the transcript can be labelled as
        // it streams rather than buffered and rewritten afterwards.
        var labeler = output.Diarize
            ? new SpeakerLabeler(await diarizer.DiarizeAsync(audio, output.Speakers, cancellationToken))
            : null;

        using var whisperFactory = WhisperFactory.FromPath(modelPath);
        Console.Error.WriteLine($"Whisper runtime: {RuntimeOptions.LoadedLibrary}");

        using var processor = BuildProcessor(whisperFactory, language);
        using var sink = TranscriptSink.Create(output.OutputPath);

        await foreach (var segment in processor.ProcessAsync(audio.Samples, cancellationToken))
        {
            var start = segment.Start + audio.Offset;
            var end = segment.End + audio.Offset;
            var text = segment.Text.Trim();

            if (labeler is not null)
            {
                text = $"{SpeakerLabeler.Format(labeler.Resolve(start, end))}: {text}";
            }

            sink.Write(
                output.Timestamps
                    ? $"[{start:hh\\:mm\\:ss\\.fff} --> {end:hh\\:mm\\:ss\\.fff}] {text}"
                    : text,
                end);
        }
    }

    private WhisperProcessor BuildProcessor(WhisperFactory factory, string language)
    {
        var builder = factory.CreateBuilder()
            .WithLanguage(ResolveLanguage(language))
            .WithThreads(options.Threads)
            // Temperature 0 with a fallback ladder: retry hotter only when a
            // decode fails the entropy/logprob checks below.
            .WithTemperature(0f)
            .WithTemperatureInc(0.2f)
            .WithEntropyThreshold(2.4f)
            .WithLogProbThreshold(-1.0f)
            .WithNoSpeechThreshold(0.6f);

        if (options.NoContext)
        {
            builder = builder.WithNoContext();
        }

        if (!string.IsNullOrWhiteSpace(options.Prompt))
        {
            builder = builder.WithPrompt(options.Prompt);
        }

        return builder
            .WithBeamSearchSamplingStrategy(beamSearch => beamSearch
                .WithBeamSize(options.BeamSize)
                .WithPatience(options.Patience))
            .Build();
    }

    /// <summary>
    /// The configured language wins unless the caller asked for something
    /// specific — kb-whisper is Swedish-only, so defaulting to detection
    /// would only invite mis-detection.
    /// </summary>
    private string ResolveLanguage(string language) =>
        string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? options.Language
            : language;
}
