using Whisper.net.LibraryLoader;

// Prefer CUDA, fall back to Vulkan, then CPU. Whisper.net probes these in order
// and skips any runtime whose native library will not load, so a CUDA build
// without an sm_120 target on Blackwell degrades to Vulkan instead of failing.
RuntimeOptions.RuntimeLibraryOrder =
[
    RuntimeLibrary.Cuda,
    RuntimeLibrary.Vulkan,
    RuntimeLibrary.Cpu,
];

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.Section));

builder.Services.AddOptions<LocalWhisperOptions>()
    .Bind(builder.Configuration.GetSection(LocalWhisperOptions.Section));

builder.Services.AddKeyedSingleton<IWhisperService, WhisperService>("openai");
builder.Services.AddKeyedSingleton<IWhisperService, LocalWhisperService>("local");


// Commanda binds a parameter by position unless it carries [Option], in which
// case it becomes a --named flag.
builder.AddCommand("OpenaiSpeechToText", async (
    [Option] string file,
    IServiceProvider serviceProvider,
    [Option] string language = "auto",
    [Option] string? output = null,
    [Option] bool timestamps = false) =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("openai");
    await whisperService.SpeechToText(file, language, new TranscriptOptions(output, timestamps));
});

builder.AddCommand("LocalSpeechToText", async (
    [Option] string file,
    IServiceProvider serviceProvider,
    [Option] string language = "auto",
    [Option] string? output = null,
    [Option] bool timestamps = false) =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("local");
    await whisperService.SpeechToText(file, language, new TranscriptOptions(output, timestamps));
});

var app = builder.Build();
await app.RunCommandsAsync(args);
