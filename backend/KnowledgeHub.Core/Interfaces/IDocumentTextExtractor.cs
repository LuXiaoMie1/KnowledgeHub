namespace KnowledgeHub.Core.Interfaces;

public interface IDocumentTextExtractor
{
    bool CanHandle(string fileExtension);   // ".pdf" / ".md"（小寫含點）
    string ExtractText(string filePath);
}
