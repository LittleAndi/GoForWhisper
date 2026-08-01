# GoForWhisper

GoForWhisper is a small .NET command-line app for converting speech to text with either:

- OpenAI transcription via the `OpenaiSpeechToText` command
- A local Whisper model via the `LocalSpeechToText` command

The local path can also label the transcript by speaker — see
[Speaker diarization](#speaker-diarization).

The project targets `.NET 10` and uses `Whisper.net` for local transcription and
`sherpa-onnx` for diarization.

## Requirements

- .NET 10 SDK
- An audio file to transcribe (wav/mp3/aiff natively; m4a/flac/wma via Media Foundation)
- For local transcription: a GGML Whisper model, downloaded automatically on first run
- For speaker diarization: two ONNX models, also downloaded on first run
- For OpenAI transcription: an OpenAI API key

## Project Layout

- `Program.cs`: command registration and app startup
- `Services/IWhisperService.cs`: OpenAI and local Whisper implementations
- `Services/AudioPreprocessor.cs`: decode, downmix, resample, trim, normalise
- `Services/SpeakerDiarizer.cs`: who spoke when, via sherpa-onnx
- `Services/SpeakerLabeler.cs`: maps transcript segments onto speaker turns
- `appsettings.json`: OpenAI configuration

## Configuration

**Local transcription needs no configuration at all.** Every `LocalWhisper` setting
has a default baked into `LocalWhisperOptions`, so a fresh clone can run
`LocalSpeechToText` immediately — no API key, no config file.

To customise anything, copy the template:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

`appsettings.json` is gitignored; `appsettings.example.json` is the committed
template. Keep real credentials out of both — use user secrets for the API key.
See [Local transcription settings](#local-transcription-settings) for what each
`LocalWhisper` value does.

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
dotnet run -- LocalSpeechToText --file .\sample.mp3
```

The language comes from configuration (`sv` by default). Pass `--language` only to
override it; `auto` falls back to the configured value rather than enabling
detection, because kb-whisper is a Swedish-only fine-tune and detection would
only add a failure mode.

Input is decoded to 16 kHz mono, level-normalised, and trimmed of leading and
trailing silence before decoding.

### Output options

Both commands accept `--output` and `--timestamps`:

| Flag              | Default      | Effect                                                                                                                  |
| ----------------- | ------------ | ----------------------------------------------------------------------------------------------------------------------- |
| `--output <path>` | _(stdout)_   | Write the transcript to a file (UTF-8, no BOM). Parent directories are created. Decode progress goes to stderr instead. |
| `--timestamps`    | off          | Prefix each line with `[hh:mm:ss.fff --> hh:mm:ss.fff]`. Bare switch; `--timestamps false` also works.                  |
| `--diarize`       | off          | Label each line with who is speaking. Local command only; see [Speaker diarization](#speaker-diarization).              |
| `--speakers <n>`  | `0` _(auto)_ | Known speaker count. Implies `--diarize`. Local command only.                                                           |

```powershell
# plain text to stdout (default)
dotnet run -- LocalSpeechToText --file .\sample.mp3

# timestamped, straight to a file
dotnet run -- LocalSpeechToText --file .\sample.mp3 --output transcript.txt --timestamps
```

Timestamps are shifted back past any trimmed silence, so they still refer to
positions in the original file. When writing to a file you get a live progress
line on stderr:

```text
  412 segments, 01:07:33 transcribed
Wrote 418 segments to C:\Dev\GoForWhisper\transcript.txt
```

The OpenAI path returns one flat string with no segment times, so `--timestamps`
has no effect there.

## Speaker diarization

`--diarize` answers "who spoke when", so an interview or a panel comes out split
by speaker rather than as one undifferentiated wall of text:

```powershell
# two speakers, and you know it up front
dotnet run -- LocalSpeechToText --file .\interview.mp3 --speakers 2 --timestamps

# let it work out the count itself
dotnet run -- LocalSpeechToText --file .\panel.mp3 --diarize
```

```text
[00:00:04.120 --> 00:00:08.640] SPEAKER 1: Välkommen till avsnitt 285.
[00:00:08.640 --> 00:00:12.310] SPEAKER 2: Tack, kul att vara här igen.
```

**Whisper itself cannot do this.** It transcribes words and has no concept of who
is talking — whisper.cpp's own `--diarize` only compares stereo channel energy
(useless on a normal mixed recording), and `--tinydiarize` needs an English-only
model that marks turn boundaries without identifying anyone. So diarization runs
as a separate pass over the same 16 kHz mono samples, via
[sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx):

1. **Segmentation** — pyannote segmentation-3.0 finds speech regions and the
   points where the active speaker changes, including overlapped speech.
2. **Embedding** — each region is reduced to a speaker embedding, a vector
   describing the voice rather than the words.
3. **Clustering** — embeddings are grouped. With `--speakers n` the count is
   fixed; otherwise clusters are merged up to `Diarization:Threshold`.

The result is then merged with Whisper's segments by **greatest temporal
overlap**. The two models cut the audio in different places — Whisper on sentence
boundaries, the segmenter on turn boundaries — so each transcript line is given
the speaker it shares the most time with. A line overlapping no detected speech
is labelled `SPEAKER ?`.

Everything runs locally on CPU; no API key and no Python. Two models
(~6 MB and ~40 MB) download to `models/` on first use. Diarization is CPU-bound
and not GPU-accelerated by default — budget roughly half the audio duration on a
desktop CPU, on top of Whisper's own decode time.

> **This works well when voices are distinct and becomes unreliable when they are
> not.** On a two-host podcast where both hosts sound alike, it collapsed both
> speakers into one cluster. Read [Reliability](#reliability--read-this-before-trusting-a-result)
> before depending on the output.

Speaker numbers are **cluster ids, not identities**. `SPEAKER 1` means "the same
voice as the other `SPEAKER 1` lines" — the numbering is arbitrary and will not
be stable across runs or files.

### Diarization settings

All under the `Diarization` section, overridable as `Diarization__Threshold=0.6`:

| Setting                         | Default                   | Purpose                                                                                                                  |
| ------------------------------- | ------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `SegmentationModelPath` / `Url` | pyannote-segmentation-3.0 | Finds turn boundaries                                                                                                    |
| `EmbeddingModelPath` / `Url`    | 3D-Speaker ERes2Net-base  | Characterises each voice. **Read [Reliability](#reliability--read-this-before-trusting-a-result) before changing**       |
| `Speakers`                      | `0`                       | Fixed speaker count; `0` decides by `Threshold`. `--speakers` overrides                                                  |
| `Threshold`                     | `0.5`                     | Cluster-merge cutoff when the count is open. Lower splits into more speakers, higher merges                              |
| `MinDurationOn`                 | `0.3`                     | Speech runs shorter than this (seconds) are dropped                                                                      |
| `MinDurationOff`                | `0.5`                     | Silences shorter than this (seconds) do not split a turn                                                                 |
| `Threads`                       | CPU count                 | ONNX Runtime threads                                                                                                     |
| `Provider`                      | `cpu`                     | ONNX execution provider: `cpu`, `cuda`, `directml`. Non-CPU needs the matching `org.k2fsa.sherpa.onnx.runtime.*` package |
| `Debug`                         | `false`                   | ONNX model diagnostics on stderr                                                                                         |

**Pass `--speakers` when you know the count.** Automatic estimation is the
weakest link: on a 38-minute two-host episode, threshold-based estimation
produced **39 speakers**, most of them under two seconds. Fixing the count avoids
that failure mode entirely.

### Reliability — read this before trusting a result

**Clustering quality degrades sharply when two voices are similar, and near that
limit the result is unstable.** This is a property of the approach, not a
configuration mistake, and it is easy to mistake for success because any single
run produces confident-looking output.

Measured on a 6-minute excerpt of a Swedish podcast with two similar-sounding
hosts, speaker count fixed at 2, varying nothing but a **time shift of a few
milliseconds** — inaudible, and semantically meaningless:

| Shift         | 0 ms | 3 ms | 6 ms | 12 ms | 24 ms | 50 ms | 100 ms | 250 ms |
| ------------- | ---- | ---- | ---- | ----- | ----- | ----- | ------ | ------ |
| ERes2NetV2    | 61%  | 3%   | 60%  | 3%    | 3%    | 3%    | 3%     | 3%     |
| ERes2Net-base | 58%  | 3%   | 3%   | 55%   | 58%   | 58%   | 58%    | 62%    |

_(share of speech time given to the second speaker; ~50% is correct, ~3% means
both hosts collapsed into one cluster)_

The same models on a control recording — one male and one female TTS voice
alternating — return **49/51 at every shift, exactly**. So the pipeline is sound;
it is these two particular voices that sit on the knife edge, and there the
outcome is close to a coin flip.

What follows from this:

- **Do not tune settings against a single run.** Re-run with a small time offset
  (`ffmpeg -ss 0.05`) before concluding that a change helped.
- **A good result on well-separated speakers says nothing about hard audio.**
  WeSpeaker VoxCeleb CAM++ fails even the trivial control (91/9), so it is worth
  avoiding outright, but passing the control does not predict real performance:
  VoxCeleb ResNet34 scores a clean 49/51 on it and still collapses on real speech.
- **Bigger is not better.** NeMo TitaNet-large is the largest model tried and among
  the worst; the 40 MB ERes2Net-base is the most stable.

**If diarization matters and your speakers sound alike, this stack is not the
right tool.** [pyannote.audio](https://github.com/pyannote/pyannote-audio) 3.1 —
the reference implementation, which this borrows only the segmentation model
from — does its own overlap-aware clustering and is materially more robust.
It needs Python and a Hugging Face token, which is exactly the dependency this
implementation avoids; that trade is the reason for the instability above, not an
accident of configuration.

To try a different embedding model, point `EmbeddingModelPath` at a new file and
`EmbeddingModelUrl` at any `.onnx` from the sherpa-onnx
[speaker-recognition release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/speaker-recongition-models)
(the `recongition` typo is theirs), then re-measure as described above.

The `sv` in these file names means _speaker verification_, not Swedish. They are
trained largely on Mandarin speech and still work on Swedish, which is the
expected result — an embedding models voice timbre, not language. So a lopsided
result is not a language-transfer problem, and there is no "Swedish" embedding
model to go looking for.

The OpenAI path has no diarization; `--diarize` there prints a warning and is
otherwise ignored.

## The local model

The default is [KBLab/kb-whisper-large](https://huggingface.co/KBLab/kb-whisper-large),
the National Library of Sweden's Whisper fine-tune trained on 50,000+ hours of
Swedish. KBLab report an average **47% WER reduction versus `openai/whisper-large-v3`**
on Swedish.

On first run of `LocalSpeechToText` the model (~2.9 GB) is downloaded to
`models/kb-whisper-large.bin`. It streams to a `.partial` sidecar first, so an
interrupted download cannot leave a truncated file that later looks valid.

To use a different model, point `LocalWhisper:ModelPath` and `LocalWhisper:ModelUrl`
somewhere else. Useful alternatives:

| Model                                     | URL                                                                                 |
| ----------------------------------------- | ----------------------------------------------------------------------------------- |
| kb-whisper-large q5_0 (~1 GB, quantised)  | `https://huggingface.co/KBLab/kb-whisper-large/resolve/main/ggml-model-q5_0.bin`    |
| kb-whisper-small (fast Swedish)           | `https://huggingface.co/KBLab/kb-whisper-small/resolve/main/ggml-model.bin`         |
| OpenAI large-v3-turbo (non-Swedish audio) | `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin` |

## GPU acceleration

Backend preference is `LocalWhisper:Backends`, tried left to right; the first
whose native library loads wins. Whisper.net skips any backend that will not
load, and the one actually selected is printed to stderr on each run:

```text
Whisper runtime: Vulkan
```

**Vulkan is the default ahead of CUDA, which is deliberate.** Measured on an
RTX 5060 Ti with kb-whisper-large, 3.7 minutes of Swedish audio, beam 5:

| Backend    | Wall time     | Throughput         |
| ---------- | ------------- | ------------------ |
| **Vulkan** | 23.5 – 24.8 s | **~9.4x realtime** |
| CUDA       | 27.0 – 28.0 s | ~8.0x realtime     |

Vulkan is ~15% faster here because ggml's Vulkan backend uses cooperative-matrix
instructions (`NV_coopmat2`) on Blackwell. This ranking is hardware-dependent —
measure on your own GPU rather than assuming CUDA wins:

```powershell
$env:LocalWhisper__Backends__0="Cuda"    # force one backend for a timing run
```

Backend choice does not affect accuracy. Each backend is deterministic across
runs, and across backends the transcripts were word-identical — differing only
in where one segment boundary fell, from floating-point accumulation order.

CUDA requires **CUDA Toolkit 13.x**. `Whisper.net.Runtime.Cuda.Windows` 1.9.1 links
against `cudart64_13.dll` and `cublas64_13.dll`, so a CUDA 12.x toolkit will not
satisfy it no matter how recent — the backend is silently skipped and Vulkan is
used instead. Installing the 13.x toolkit puts those DLLs on `PATH` and the CUDA
backend then loads with no code change:

```powershell
winget install Nvidia.CUDA --version 13.3
```

Two things that are easy to get wrong here:

- **The package name matters.** `Whisper.net.Runtime.Cuda12.Windows` deploys to
  `runtimes/cuda12/win-x64`, but the loader only ever looks in
  `runtimes/cuda/win-x64` (the directory is derived from the `RuntimeLibrary` enum
  name). Referencing the `Cuda12` variant means the loader never sees it at all.
- **A partial CUDA install crashes rather than falling back.** If `cudart` is
  resolvable but `cublas` is not, the managed availability probe succeeds, the
  native load then fails, and ggml aborts. Either have all of `cudart64_13.dll`,
  `cublas64_13.dll` and `cublasLt64_13.dll` reachable, or none of them.

Set `LocalWhisper:Debug` to `true` to see the loader's backend decisions on
stderr — it is the only way to find out why a backend was skipped.

## Local transcription settings

All under the `LocalWhisper` section, overridable with environment variables such
as `LocalWhisper__BeamSize=8`:

| Setting         | Default                       | Purpose                                                                                                               |
| --------------- | ----------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `ModelPath`     | `models/kb-whisper-large.bin` | Where the model is read from and downloaded to                                                                        |
| `ModelUrl`      | kb-whisper-large GGML         | Source used when `ModelPath` is missing                                                                               |
| `Language`      | `sv`                          | Decoding language                                                                                                     |
| `Prompt`        | _(empty)_                     | Vocabulary bias: proper nouns, domain terms, orthography. Keep it short — long prompts leak into the transcript       |
| `BeamSize`      | `5`                           | Beam search width; higher is slower and usually more accurate                                                         |
| `Patience`      | `1.0`                         | Beam search patience                                                                                                  |
| `NoContext`     | `false`                       | Set `true` if you hit repetition loops — stops a bad segment seeding the next, at the cost of cross-segment coherence |
| `TrimSilence`   | `true`                        | Drops leading/trailing silence, the most common source of hallucinated filler                                         |
| `TargetRmsDbfs` | `-20`                         | Level normalisation target; gain is capped so peaks cannot clip                                                       |
| `Threads`       | CPU count                     | Decoder threads                                                                                                       |
| `Debug`         | `false`                       | Emit Whisper.net loader and model diagnostics on stderr                                                               |
| `Backends`      | `["Vulkan","Cuda","Cpu"]`     | Backend preference order; see [GPU acceleration](#gpu-acceleration)                                                   |

Decoding runs at temperature 0 with a fallback ladder (`TemperatureInc` 0.2) gated
on entropy, log-probability, and no-speech thresholds, so a hotter retry only
happens when a decode actually fails those checks.

## Notes

- `Whisper.net` has VAD internally but does not expose it on `WhisperProcessorBuilder`
  in 1.9.1, so silence handling is done in `AudioPreprocessor` instead.
- Audio is fully buffered in memory (~230 MB per hour as 32-bit float samples).
  Fine for clips; worth streaming if you process long recordings.
- The OpenAI path sends `language: null` for auto-detection. `"auto"` is a
  whisper.cpp convention and is not a valid value for the OpenAI API.
