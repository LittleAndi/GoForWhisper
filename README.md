# GoForWhisper

GoForWhisper is a small .NET command-line app for converting speech to text with either:

- OpenAI transcription via the `OpenaiSpeechToText` command
- A local Whisper model via the `LocalSpeechToText` command

The project targets `.NET 10` and uses `Whisper.net` for local transcription.

## Requirements

- .NET 10 SDK
- An audio file to transcribe
- For local transcription: the `ggml-base.bin` Whisper model
- For OpenAI transcription: an OpenAI API key

## Project Layout

- `Program.cs`: command registration and app startup
- `Services/IWhisperService.cs`: OpenAI and local Whisper implementations
- `appsettings.json`: OpenAI configuration

## Configuration

The app reads OpenAI settings from configuration. For local development, use .NET user secrets for the API key and keep only safe defaults in `appsettings.json`.

Recommended `appsettings.json` contents:

```json
{
  "OpenAI": {
    "ApiKey": "",
    "Model": "gpt-4o-mini-transcribe"
  }
}
```

Initialize user secrets for the project and store the real key there:

```powershell
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "your-real-api-key"
dotnet user-secrets set "OpenAI:Model" "gpt-4o-mini-transcribe"
dotnet user-secrets list
```

With the current host setup, user secrets are loaded automatically when the app runs in the `Development` environment. In PowerShell:

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run -- OpenaiSpeechToText --file .\sample.mp3 --language en
```

Do not keep a real API key in `appsettings.json`.

## Build

```powershell
dotnet build .\GoForWhisper.sln
```

## Usage

Run commands with `dotnet run --` followed by the command name and arguments.

### OpenAI transcription

```powershell
dotnet run -- OpenaiSpeechToText --file .\sample.mp3 --language en
```

If you want automatic language detection, use `auto` or omit the argument if your command invocation supplies the default.

### Local Whisper transcription

```powershell
dotnet run -- LocalSpeechToText --file .\sample.mp3 --language en
```

The local implementation currently reads MP3 input, resamples it to 16 kHz WAV in memory, and prints each transcription segment to the console.

## Getting `ggml-base.bin`

You have two ways to get the model file.

### Option 1: Let the app download it automatically

If `ggml-base.bin` is not present in the project working directory, `LocalWhisperService` will download the base GGML model automatically on first run of `LocalSpeechToText` and save it as:

```text
ggml-base.bin
```

So in the common case, you can just run:

```powershell
dotnet run -- LocalSpeechToText --file .\sample.mp3 --language en
```

and the model will be fetched for you.

### Option 2: Download it manually

Download `ggml-base.bin` from one of the official Whisper model sources and place it in the repository root next to `GoForWhisper.csproj`.

Common sources:

- `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin`
- `https://github.com/ggerganov/whisper.cpp`

Example with PowerShell:

```powershell
Invoke-WebRequest \
  -Uri "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin" \
  -OutFile ".\ggml-base.bin"
```

After that, local transcription will use the existing file instead of downloading it.

## Notes

- `LocalAudioToText` is registered but not implemented yet.
- The current local transcription path uses `Mp3FileReader`, so MP3 input is the safe default.
- `ggml-base.bin` is already present in this repository snapshot, so local transcription may work immediately.
