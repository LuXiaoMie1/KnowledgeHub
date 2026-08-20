namespace KnowledgeHub.Core;

public static class ConversationTitle
{
    private const int MaxLength = 30;

    public static string From(string firstMessage)
    {
        var t = firstMessage.ReplaceLineEndings(" ").Trim();
        return t.Length <= MaxLength ? t : t[..MaxLength];
    }
}
