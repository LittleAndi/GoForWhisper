namespace Services;

/// <summary>
/// Resolves a model file on disk, downloading it on first use.
/// </summary>
public static class ModelDownloader
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// Returns the full path to <paramref name="modelPath"/>, fetching it from
    /// <paramref name="modelUrl"/> if it is not there yet.
    /// </summary>
    public static async Task<string> EnsureAsync(
        string modelPath,
        string modelUrl,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(modelPath);
        if (File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Download to a sidecar first so an interrupted run cannot leave a
        // truncated file that later looks like a valid model.
        var partial = path + ".partial";

        Console.Error.WriteLine($"Downloading model to {path} ...");

        using (var response = await Http.GetAsync(
                   modelUrl,
                   HttpCompletionOption.ResponseHeadersRead,
                   cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(partial);
            await CopyWithProgressAsync(source, destination, total, cancellationToken);
        }

        Console.Error.WriteLine();
        File.Move(partial, path, overwrite: true);
        return path;
    }

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long? total,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 20];
        long copied = 0;
        var lastReport = -1;

        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;

            if (total is not > 0)
            {
                continue;
            }

            var percent = (int)(copied * 100 / total.Value);
            if (percent == lastReport)
            {
                continue;
            }

            lastReport = percent;
            Console.Error.Write($"\r  {percent,3}%  ({copied / (1024 * 1024)} / {total.Value / (1024 * 1024)} MB)");
        }
    }
}
