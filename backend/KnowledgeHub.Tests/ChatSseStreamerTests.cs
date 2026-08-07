using System.Text;
using KnowledgeHub.Api.Sse;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

public class ChatSseStreamerTests
{
    private sealed class FakeChat(IEnumerable<string> tokens, Exception? throwAfter = null) : IChatService
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var t in tokens) { yield return t; await Task.Yield(); }
            if (throwAfter is not null) throw throwAfter;
        }
    }

    // 模擬 client 斷線／請求被取消：吐出一個 token 後，觸發傳入的 CancellationTokenSource
    // 並丟出對應該 token 的 OperationCanceledException（模擬 await foreach 因取消而中止）。
    private sealed class CancelingChat(CancellationTokenSource cts) : IChatService
    {
        public async IAsyncEnumerable<string> StreamAnswerAsync(
            string message, IReadOnlyList<ChatTurn> history,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "前半";
            await Task.Yield();
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
        }
    }

    private static async Task<string> RunAsync(IChatService chat, RetrievalContext context)
    {
        using var stream = new MemoryStream();
        await new ChatSseStreamer(chat, context, NullLogger<ChatSseStreamer>.Instance).StreamAsync(
            stream, "問題", [], CancellationToken.None);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task 有檢索結果_依序發token_sources_done()
    {
        var context = new RetrievalContext();
        context.Results.Add(new ChunkSearchResult(
            Guid.NewGuid(), Guid.NewGuid(), "sop.md", 2, "內容", 0.1));

        var output = await RunAsync(new FakeChat(["你", "好"]), context);

        Assert.Contains("event: token\ndata: {\"text\":\"你\"}", output);
        Assert.Contains("event: sources\n", output);
        Assert.Contains("\"fileName\":\"sop.md\"", output);
        Assert.Contains("event: done\n", output);
        // 順序：最後一個 token 在 sources 前，sources 在 done 前
        Assert.True(output.IndexOf("event: sources") > output.LastIndexOf("event: token"));
        Assert.True(output.IndexOf("event: done") > output.IndexOf("event: sources"));
    }

    [Fact]
    public async Task 模型沒查庫_不發sources()
    {
        var output = await RunAsync(new FakeChat(["嗨"]), new RetrievalContext());
        Assert.DoesNotContain("event: sources", output);
        Assert.Contains("event: done\n", output);
    }

    [Fact]
    public async Task 串流中途例外_發error後結束_不發done()
    {
        var output = await RunAsync(
            new FakeChat(["前半"], throwAfter: new InvalidOperationException("boom")), new RetrievalContext());
        Assert.Contains("event: token\ndata: {\"text\":\"前半\"}", output);
        Assert.Contains("event: error\n", output);
        Assert.DoesNotContain("event: done", output);
    }

    [Fact]
    public async Task 請求被取消_靜默結束_不拋出也不發error()
    {
        var cts = new CancellationTokenSource();
        using var stream = new MemoryStream();
        var streamer = new ChatSseStreamer(
            new CancelingChat(cts), new RetrievalContext(), NullLogger<ChatSseStreamer>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            streamer.StreamAsync(stream, "問題", [], cts.Token));

        Assert.Null(ex);
        var output = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("event: error", output);
        Assert.DoesNotContain("event: done", output);
    }
}
