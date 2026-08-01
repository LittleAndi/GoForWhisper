namespace Services;

public sealed class LocalWhisperOptions
{
    public const string Section = "LocalWhisper";

    /// <summary>
    /// Path the GGML model is read from, and downloaded to if missing.
    /// </summary>
    public string ModelPath { get; set; } = "models/kb-whisper-large.bin";

    /// <summary>
    /// Source for the model when <see cref="ModelPath"/> does not exist yet.
    /// Defaults to the National Library of Sweden's Swedish-tuned large model.
    /// </summary>
    public string ModelUrl { get; set; } =
        "https://huggingface.co/KBLab/kb-whisper-large/resolve/main/ggml-model.bin";

    /// <summary>
    /// kb-whisper is a Swedish-only fine-tune, so "auto" detection only adds risk.
    /// </summary>
    public string Language { get; set; } = "sv";

    /// <summary>
    /// Vocabulary bias: proper nouns, domain terms, and the orthography you want.
    /// Kept short deliberately — long prompts leak into the transcript.
    /// </summary>
    public string? Prompt { get; set; }

    public int BeamSize { get; set; } = 5;

    public float Patience { get; set; } = 1.0f;

    /// <summary>
    /// Starts a fresh decode context per segment. Costs cross-segment coherence
    /// but stops one bad segment from seeding a repetition loop in the next.
    /// </summary>
    public bool NoContext { get; set; }

    /// <summary>
    /// Drops leading/trailing near-silence, the most common source of
    /// hallucinated filler. Timestamps are shifted back to real time.
    /// </summary>
    public bool TrimSilence { get; set; } = true;

    /// <summary>
    /// Target RMS for level normalisation, in dBFS. Gain is clamped so the
    /// peak never exceeds <see cref="PeakCeiling"/>.
    /// </summary>
    public float TargetRmsDbfs { get; set; } = -20f;

    public float PeakCeiling { get; set; } = 0.98f;

    public int Threads { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Emits Whisper.net's native loader and model diagnostics on stderr. The
    /// loader silently falls back between GPU backends, so this is the only way
    /// to see why a given backend was skipped.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    /// Backend preference order, tried left to right; the first one whose native
    /// library loads wins. Values are <c>RuntimeLibrary</c> names (Cuda, Vulkan,
    /// Cpu, CoreML, OpenVino, CpuNoAvx).
    /// <para>
    /// Vulkan leads deliberately: on an RTX 5060 Ti it measured ~15% faster than
    /// CUDA on kb-whisper-large (9.4x vs 8.0x realtime), because ggml's Vulkan
    /// backend uses cooperative-matrix instructions on Blackwell. That ranking is
    /// hardware-dependent — measure before assuming it holds elsewhere.
    /// </para>
    /// Read in Program.cs before the host is built, not through this type.
    /// </summary>
    public string[] Backends { get; set; } = ["Vulkan", "Cuda", "Cpu"];
}
