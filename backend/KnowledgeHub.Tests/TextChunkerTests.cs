using KnowledgeHub.Core;

public class TextChunkerTests
{
    [Fact]
    public void 空字串回空清單()
        => Assert.Empty(TextChunker.Split(""));

    [Fact]
    public void 純空白也回空清單()
        => Assert.Empty(TextChunker.Split("   \n  "));

    [Fact]
    public void 短於chunkSize回單片且等於原文()
    {
        var chunks = TextChunker.Split("短文", chunkSize: 500);
        Assert.Single(chunks);
        Assert.Equal("短文", chunks[0]);
    }

    [Fact]
    public void 長文切片_片長與重疊正確()
    {
        // 1200 字元、chunkSize 500、overlap 10%(50) → step 450 → 起點 0/450/900 → 長度 500/500/300
        var text = string.Concat(Enumerable.Range(0, 1200).Select(i => (char)('A' + i % 26)));
        var chunks = TextChunker.Split(text, chunkSize: 500, overlapRatio: 0.1);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(500, chunks[0].Length);
        Assert.Equal(500, chunks[1].Length);
        Assert.Equal(300, chunks[2].Length);
        // 重疊驗證：第 2 片的前 50 字 == 第 1 片的最後 50 字
        Assert.Equal(chunks[0][^50..], chunks[1][..50]);
        // 內容無遺漏：去重疊後串回原文
        Assert.Equal(text, chunks[0] + chunks[1][50..] + chunks[2][50..]);
    }

    [Fact]
    public void 中文以字元計數()
    {
        var text = string.Concat(Enumerable.Repeat("知識庫測試", 30)); // 150 字元
        var chunks = TextChunker.Split(text, chunkSize: 100, overlapRatio: 0.1);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(100, chunks[0].Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void chunkSize非正數丟例外(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("x", chunkSize: size));
}
