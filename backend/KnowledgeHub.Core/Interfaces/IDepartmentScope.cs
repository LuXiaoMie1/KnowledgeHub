namespace KnowledgeHub.Core.Interfaces;

/// <summary>
/// 檢索時使用的部門範圍。刻意獨立於 <see cref="ICurrentUser"/>：ICurrentUser 需要
/// HttpContext 的 department claim，沒有使用者身分的管道（例如匿名 bot）呼叫會丟例外
/// （見 ICurrentUser 實作的類別註解）。RetrievalPlugin 只依賴這個較小的介面，才能在
/// 不同管道注入不同的部門範圍來源，而不必讓每個管道都偽造一個 ICurrentUser。
/// </summary>
public interface IDepartmentScope
{
    IReadOnlyList<string> Departments { get; }
}
