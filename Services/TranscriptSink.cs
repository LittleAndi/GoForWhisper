using System.Text;

namespace Services;

/// <param name="OutputPath">File to write to; stdout when null or empty.</param>
/// <param name="Timestamps">Prefix each line with its time range.</param>
public sealed record TranscriptOptions(string? OutputPath = null, bool Timestamps = false);

/// <summary>
/// Writes transcript lines to a file or stdout. When the destination is a file
/// there is nothing to watch, so decode progress is reported on stderr instead —
/// a long recording otherwise looks like it has stalled.
/// </summary>
public sealed class TranscriptSink : IDisposable
{
    private readonly TextWriter writer;
    private readonly string? path;
    private int segments;

    private TranscriptSink(TextWriter writer, string? path)
    {
        this.writer = writer;
        this.path = path;
    }

    private bool OwnsWriter => path is not null;

    public static TranscriptSink Create(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new TranscriptSink(Console.Out, path: null);
        }

        var full = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // UTF-8 without a BOM — the transcripts are plain text, often piped onward.
        var stream = new StreamWriter(full, append: false, new UTF8Encoding(false));
        return new TranscriptSink(stream, full);
    }

    public void Write(string line, TimeSpan position)
    {
        writer.WriteLine(line);
        segments++;

        if (OwnsWriter)
        {
            Console.Error.Write($"\r  {segments} segments, {position:hh\\:mm\\:ss} transcribed");
        }
    }

    public void Dispose()
    {
        writer.Flush();

        if (!OwnsWriter)
        {
            return;
        }

        writer.Dispose();
        Console.Error.WriteLine($"\rWrote {segments} segments to {path}");
    }
}
