namespace KnowledgeHub.Core;

public static class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 500, double overlapRatio = 0.1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(chunkSize, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlapRatio, 1.0);

        text = (text ?? "").Trim();
        if (text.Length == 0) return [];

        var overlap = (int)(chunkSize * overlapRatio);
        var step = chunkSize - overlap;
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));
            if (start + length >= text.Length) break;
        }
        return chunks;
    }
}
