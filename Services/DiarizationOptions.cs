namespace Services;

public sealed class DiarizationOptions
{
    public const string Section = "Diarization";

    /// <summary>
    /// Pyannote segmentation-3.0, exported to ONNX. Decides <em>when</em> someone
    /// is speaking and where turns change — it does not identify who.
    /// </summary>
    public string SegmentationModelPath { get; set; } = "models/pyannote-segmentation-3-0.onnx";

    public string SegmentationModelUrl { get; set; } =
        "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx";

    /// <summary>
    /// Speaker embedding model, run over each segment the segmenter found. The
    /// embeddings are what actually get clustered into speakers.
    /// <para>
    /// ERes2Net-base is the default because it was the most stable of those
    /// tested, not because it is the largest — the 101 MB TitaNet-large was among
    /// the worst. Judge a replacement by re-running the same audio at a few small
    /// time offsets, never by a single run; see the reliability section of the
    /// README for why. (The <c>sv</c> in the file name is "speaker verification",
    /// not Swedish.)
    /// </para>
    /// </summary>
    public string EmbeddingModelPath { get; set; } = "models/3dspeaker-eres2net-base.onnx";

    public string EmbeddingModelUrl { get; set; } =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx";

    /// <summary>
    /// Exact number of speakers, when it is known up front. Zero means "work it
    /// out", which falls back to <see cref="Threshold"/>. Telling it the real
    /// count is markedly more reliable than letting it guess.
    /// </summary>
    public int Speakers { get; set; }

    /// <summary>
    /// Cluster-merge cutoff used only when <see cref="Speakers"/> is zero. Lower
    /// splits more eagerly (more speakers), higher merges more.
    /// </summary>
    public float Threshold { get; set; } = 0.5f;

    /// <summary>Speech runs shorter than this (seconds) are discarded.</summary>
    public float MinDurationOn { get; set; } = 0.3f;

    /// <summary>Silences shorter than this (seconds) do not split a turn.</summary>
    public float MinDurationOff { get; set; } = 0.5f;

    public int Threads { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// ONNX Runtime execution provider: <c>cpu</c>, <c>cuda</c>, or <c>directml</c>.
    /// The bundled runtime package is CPU-only, so anything else needs the matching
    /// <c>org.k2fsa.sherpa.onnx.runtime.*</c> package as well.
    /// </summary>
    public string Provider { get; set; } = "cpu";

    public bool Debug { get; set; }
}
