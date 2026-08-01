using Whisper.net.LibraryLoader;

// Touching RuntimeOptions runs Whisper.net's static backend probe, which loads
// a native library immediately — so the preference order has to be set before
// anything else reaches into Whisper.net. That means reading configuration here,
// ahead of the host, rather than from LocalWhisperOptions.
var bootstrap = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var backends = bootstrap.GetSection($"{LocalWhisperOptions.Section}:Backends").Get<string[]>()
    ?? new LocalWhisperOptions().Backends;

if (backends is { Length: > 0 })
{
    var order = new List<RuntimeLibrary>();
    foreach (var name in backends)
    {
        if (Enum.TryParse<RuntimeLibrary>(name, ignoreCase: true, out var library))
        {
            order.Add(library);
        }
        else
        {
            Console.Error.WriteLine($"Unknown backend '{name}' ignored.");
        }
    }

    if (order.Count > 0)
    {
        RuntimeOptions.RuntimeLibraryOrder = order;
    }
}

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.Section));

builder.Services.AddOptions<LocalWhisperOptions>()
    .Bind(builder.Configuration.GetSection(LocalWhisperOptions.Section));

builder.Services.AddOptions<DiarizationOptions>()
    .Bind(builder.Configuration.GetSection(DiarizationOptions.Section));

builder.Services.AddSingleton<SpeakerDiarizer>();

builder.Services.AddKeyedSingleton<IWhisperService, WhisperService>("openai");
builder.Services.AddKeyedSingleton<IWhisperService, LocalWhisperService>("local");


// Commanda binds a parameter by position unless it carries [Option], in which
// case it becomes a --named flag.
builder.AddCommand("OpenaiSpeechToText", async (
    [Option] string file,
    IServiceProvider serviceProvider,
    [Option] string language = "auto",
    [Option] string? output = null,
    [Option] bool timestamps = false,
    [Option] bool diarize = false) =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("openai");
    await whisperService.SpeechToText(
        file,
        language,
        new TranscriptOptions(output, timestamps, diarize));
});

builder.AddCommand("LocalSpeechToText", async (
    [Option] string file,
    IServiceProvider serviceProvider,
    [Option] string language = "auto",
    [Option] string? output = null,
    [Option] bool timestamps = false,
    [Option] bool diarize = false,
    [Option] int speakers = 0) =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("local");
    await whisperService.SpeechToText(
        file,
        language,
        // Asking for a speaker count is only ever meant as a request to diarize.
        new TranscriptOptions(output, timestamps, diarize || speakers > 0, speakers));
});

var app = builder.Build();
await app.RunCommandsAsync(args);
