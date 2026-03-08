using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.Ggml;

namespace Services;

public interface IWhisperService
{
    Task SpeechToText(string file, string language, CancellationToken cancellationToken = default);
}

public class WhisperService(IOptions<OpenAIOptions> options) : IWhisperService
{
    private readonly OpenAIOptions options = options.Value;

    public async Task SpeechToText(string file, string language, CancellationToken cancellationToken = default)
    {
        OpenAIClient openAIClient = new(options.ApiKey);

        var client = openAIClient.GetAudioClient(options.Model);
        var result = await client.TranscribeAudioAsync(file, options: new OpenAI.Audio.AudioTranscriptionOptions { Language = language });
        System.Console.WriteLine(result.Value.Text);
    }
}

public class LocalWhisperService() : IWhisperService
{
    const string modelName = "ggml-base.bin";
    public async Task SpeechToText(string file, string language = "auto", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelName))
        {
            using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(GgmlType.Base, cancellationToken: cancellationToken);
            using var fileWriter = File.OpenWrite(modelName);
            await modelStream.CopyToAsync(fileWriter, cancellationToken);
        }

        using var whisperFactory = WhisperFactory.FromPath(modelName);

        using var processor = whisperFactory.CreateBuilder()
            // .WithSegmentEventHandler(OnNewSegment)
            //.WithTranslate()
            .WithLanguage(language)
            .Build();

        using var filestream = File.OpenRead(file);
        using var wavStream = new MemoryStream();
        using var reader = new Mp3FileReader(filestream);
        var resampler = new WdlResamplingSampleProvider(reader.ToSampleProvider(), 16000);
        WaveFileWriter.WriteWavFileToStream(wavStream, resampler.ToWaveProvider16());

        wavStream.Seek(0, SeekOrigin.Begin);

        var result = processor.ProcessAsync(wavStream, cancellationToken);
        await foreach (var segment in result)
        {
            Console.WriteLine($"CSSS {segment.Start} ==> {segment.End} : {segment.Text}");
        }

    }

    private static void OnNewSegment(SegmentData e)
    {
        Console.WriteLine($"CSSS {e.Start} ==> {e.End} : {e.Text}");
    }
}