using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Extraction;

public class MarkdownTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileExtension) => fileExtension == ".md";

    public string ExtractText(string filePath)
    {
        var text = File.ReadAllText(filePath);
        if (!text.StartsWith("---")) return text;

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return text;
        var bodyStart = text.IndexOf('\n', end + 1);
        return bodyStart < 0 ? "" : text[(bodyStart + 1)..];
    }
}
