using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

/// <summary>
/// 組裝 Semantic Kernel 執行個體：固定掛 retrieval plugin，email plugin 是否掛上由呼叫端
/// 決定——bot 管道（匿名、無使用者身分）絕不可掛 EmailPlugin，見 Program.cs 呼叫處註解。
/// 抽成獨立類別是為了讓 web／bot 兩條管道共用同一份組裝邏輯，也讓「bot kernel 不含
/// EmailPlugin」這件事可以脫離 Program.cs 的啟動流程單獨測試。
/// </summary>
public static class KernelFactory
{
    public static Kernel Build(
        string chatModel, Uri endpoint, HttpClient httpClient,
        RetrievalPlugin retrieval, EmailPlugin? email)
    {
        var kb = Kernel.CreateBuilder();
        kb.AddOpenAIChatCompletion(
            modelId: chatModel,
            endpoint: endpoint,
            apiKey: "unused", // 真認證靠 httpClient 上掛的 OAuth handler，這裡 SK 連接器要求非空字串
            httpClient: httpClient);
        var kernel = kb.Build();
        kernel.Plugins.AddFromObject(retrieval, "retrieval");
        if (email is not null) kernel.Plugins.AddFromObject(email, "email");
        return kernel;
    }
}
