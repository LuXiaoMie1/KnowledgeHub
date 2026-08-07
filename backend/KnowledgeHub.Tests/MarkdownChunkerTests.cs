using KnowledgeHub.Core;

public class MarkdownChunkerTests
{
    [Fact]
    public void 空字串回空清單()
        => Assert.Empty(MarkdownChunker.Split(""));

    [Fact]
    public void 無標題退回固定切片()
    {
        var text = new string('字', 1200);
        Assert.Equal(TextChunker.Split(text), MarkdownChunker.Split(text));
    }

    [Fact]
    public void 依標題分段_各片帶標題路徑前綴()
    {
        var md = "# 系統\n總覽說明\n## 重開機流程\n步驟一步驟二\n## 錯誤代碼\nE01 代表斷線";
        var chunks = MarkdownChunker.Split(md);
        Assert.Equal(3, chunks.Count);
        Assert.StartsWith("【系統】\n", chunks[0]);
        Assert.Contains("總覽說明", chunks[0]);
        Assert.StartsWith("【系統 > 重開機流程】\n", chunks[1]);
        Assert.Contains("步驟一步驟二", chunks[1]);
        Assert.StartsWith("【系統 > 錯誤代碼】\n", chunks[2]);
        Assert.Contains("E01 代表斷線", chunks[2]);
    }

    [Fact]
    public void 低階標題出現時_路徑收斂到該階()
    {
        var md = "# A\n## B\n內容一\n# C\n內容二";
        var chunks = MarkdownChunker.Split(md);
        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("【A > B】\n", chunks[0]);
        Assert.StartsWith("【C】\n", chunks[1]);
    }

    [Fact]
    public void 標題下無內容_不產生chunk()
    {
        var md = "# 只有標題\n## 也只有標題\n有內容";
        var chunks = MarkdownChunker.Split(md);
        Assert.Single(chunks);
        Assert.Contains("有內容", chunks[0]);
    }

    [Fact]
    public void 超長段落_細切且每片都帶前綴()
    {
        var md = "# 長章節\n" + new string('字', 1200);
        var chunks = MarkdownChunker.Split(md, chunkSize: 500, overlapRatio: 0.1);
        Assert.Equal(3, chunks.Count);
        Assert.All(chunks, c => Assert.StartsWith("【長章節】\n", c));
    }

    [Fact]
    public void CRLF文件_多行段落不殘留控制字元()
    {
        var md = "# 標題\r\n行一\r\n行二";
        var chunks = MarkdownChunker.Split(md);
        Assert.Single(chunks);
        Assert.DoesNotContain("\r", chunks[0]);
        Assert.Contains("行一\n行二", chunks[0]);
    }
}
