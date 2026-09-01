using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using KnowledgeHub.Infrastructure.Extraction;

public class DocxTextExtractorTests
{
    private static string WriteTempDocx(params OpenXmlElement[] bodyChildren)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(bodyChildren));
        main.Document.Save();
        return path;
    }

    private static Paragraph Para(string text) => new(new Run(new Text(text)));

    [Fact]
    public void 抽取段落文字_一段一行()
    {
        var path = WriteTempDocx(Para("第一段內容"), Para("第二段內容"));
        var text = new DocxTextExtractor().ExtractText(path);
        Assert.Contains("第一段內容", text);
        Assert.Contains("第二段內容", text);
        Assert.True(text.IndexOf("第一段內容") < text.IndexOf("第二段內容"));
        File.Delete(path);
    }

    [Fact]
    public void 表格儲存格文字一併抽取()
    {
        var table = new Table(new TableRow(
            new TableCell(Para("欄位名稱")), new TableCell(Para("申請人簽章"))));
        var path = WriteTempDocx(Para("表單說明"), table);
        var text = new DocxTextExtractor().ExtractText(path);
        Assert.Contains("表單說明", text);
        Assert.Contains("欄位名稱", text);
        Assert.Contains("申請人簽章", text);
        File.Delete(path);
    }

    [Fact]
    public void 只處理docx副檔名()
    {
        var extractor = new DocxTextExtractor();
        Assert.True(extractor.CanHandle(".docx"));
        Assert.False(extractor.CanHandle(".doc"));
        Assert.False(extractor.CanHandle(".pdf"));
    }
}
