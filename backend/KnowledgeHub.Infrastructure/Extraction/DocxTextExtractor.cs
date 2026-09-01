using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Infrastructure.Extraction;

public class DocxTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(string fileExtension) => fileExtension == ".docx";

    public string ExtractText(string filePath)
    {
        var sb = new StringBuilder();
        using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";
        // 逐段落取 InnerText（表格儲存格內容也是段落，一併涵蓋），一段一行。
        foreach (var p in body.Descendants<Paragraph>())
            sb.AppendLine(p.InnerText);
        return sb.ToString();
    }
}
