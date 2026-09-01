using KnowledgeHub.Core;

public class ConversationTitleTests
{
    [Fact]
    public void 短訊息原樣_換行折成空白()
        => Assert.Equal("出差 交通費怎麼報", ConversationTitle.From("出差\n交通費怎麼報"));

    [Fact]
    public void 超過30字截斷()
    {
        var title = ConversationTitle.From(new string('問', 50));
        Assert.Equal(30, title.Length);
    }
}
