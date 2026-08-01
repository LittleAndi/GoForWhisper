namespace Services;

/// <summary>
/// Attributes transcript segments to speakers.
/// <para>
/// Whisper and the diarizer cut the audio at different places — Whisper on
/// sentence boundaries, the diarizer on turn boundaries — so the two segment
/// lists never align. Each transcript segment is therefore given the speaker it
/// shares the most time with, which is robust to the boundaries disagreeing by
/// a word or two.
/// </para>
/// </summary>
public sealed class SpeakerLabeler(IReadOnlyList<SpeakerTurn> turns)
{
    private readonly SpeakerTurn[] turns = [.. turns.OrderBy(turn => turn.Start)];

    /// <summary>
    /// The speaker occupying most of <paramref name="start"/>..<paramref name="end"/>,
    /// or null when the range overlaps no turn at all — which happens when
    /// Whisper transcribes something the segmenter judged to be non-speech.
    /// </summary>
    public int? Resolve(TimeSpan start, TimeSpan end)
    {
        var best = -1;
        var bestOverlap = TimeSpan.Zero;

        foreach (var turn in turns)
        {
            // Sorted by start, so once a turn begins after our range ends,
            // no later turn can overlap either.
            if (turn.Start >= end)
            {
                break;
            }

            if (turn.End <= start)
            {
                continue;
            }

            var overlap = Min(turn.End, end) - Max(turn.Start, start);
            if (overlap > bestOverlap)
            {
                bestOverlap = overlap;
                best = turn.Speaker;
            }
        }

        return best < 0 ? null : best;
    }

    /// <summary>
    /// Renders a speaker as a stable, 1-based label. Diarizer speaker numbers are
    /// arbitrary cluster ids — they carry no identity beyond "not the other one".
    /// </summary>
    public static string Format(int? speaker) =>
        speaker is null ? "SPEAKER ?" : $"SPEAKER {speaker + 1}";

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
