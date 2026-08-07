namespace KnowledgeHub.Core;

/// <summary>Markdown 標題感知切片：依標題分段、每片前綴標題路徑；全文無標題時退回 TextChunker 固定切片。</summary>
public static class MarkdownChunker
{
    public static IReadOnlyList<string> Split(string text, int chunkSize = 500, double overlapRatio = 0.1)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return [];

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        if (!lines.Any(IsHeading)) return TextChunker.Split(text, chunkSize, overlapRatio);

        var chunks = new List<string>();
        var path = new List<(int Level, string Title)>();
        var body = new List<string>();

        void Flush()
        {
            var content = string.Join("\n", body).Trim();
            body.Clear();
            if (content.Length == 0) return;
            var prefix = path.Count == 0 ? "" : $"【{string.Join(" > ", path.Select(p => p.Title))}】\n";
            foreach (var piece in TextChunker.Split(content, chunkSize, overlapRatio))
                chunks.Add(prefix + piece);
        }

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                Flush();
                var trimmed = line.TrimStart();
                var level = trimmed.TakeWhile(c => c == '#').Count();
                path.RemoveAll(p => p.Level >= level);
                path.Add((level, trimmed[level..].Trim()));
            }
            else body.Add(line);
        }
        Flush();
        return chunks;
    }

    private static bool IsHeading(string line)
    {
        var t = line.TrimStart();
        var hashes = t.TakeWhile(c => c == '#').Count();
        return hashes is >= 1 and <= 6 && t.Length > hashes && t[hashes] == ' ';
    }
}
