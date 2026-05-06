var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.Section));

builder.Services.AddKeyedSingleton<IWhisperService, WhisperService>("openai");
builder.Services.AddKeyedSingleton<IWhisperService, LocalWhisperService>("local");


builder.AddCommand("OpenaiSpeechToText", async (string file, IServiceProvider serviceProvider, string language = "auto") =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("openai");
    await whisperService.SpeechToText(file, language);
});

builder.AddCommand("LocalSpeechToText", async (string file, IServiceProvider serviceProvider, string language = "auto") =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("local");
    await whisperService.SpeechToText(file, language);
});

builder.AddCommand("LocalAudioToText", async (IServiceProvider serviceProvider) =>
{
    var whisperService = serviceProvider.GetRequiredKeyedService<IWhisperService>("local");

});

var app = builder.Build();
await app.RunCommandsAsync(args);