namespace Services;

/// <summary>
/// Resolves the GGML model file, downloading it on first use.
/// </summary>
public static class WhisperModelProvider
{
    public static Task<string> EnsureModelAsync(
        LocalWhisperOptions options,
        CancellationToken cancellationToken = default) =>
        ModelDownloader.EnsureAsync(options.ModelPath, options.ModelUrl, cancellationToken);
}
