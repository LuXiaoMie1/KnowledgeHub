using System.Text;
using KnowledgeHub.Core.Interfaces;
using UglyToad.PdfPig;

namespace KnowledgeHub.Infrastructure.Extraction;

public class PdfTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileExtension) => fileExtension == ".pdf";

    public string ExtractText(string filePath)
    {
        var sb = new StringBuilder();
        using var pdf = PdfDocument.Open(filePath);
        foreach (var page in pdf.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}
