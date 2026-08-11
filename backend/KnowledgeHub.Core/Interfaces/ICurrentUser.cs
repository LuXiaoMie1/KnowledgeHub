namespace KnowledgeHub.Core.Interfaces;

public interface ICurrentUser
{
    /// <summary>
    /// 恰有一個部門時才可用；使用者屬於多個部門時丟例外（多部門場景一律改用 <see cref="Departments"/>）。
    /// </summary>
    string Department { get; }

    /// <summary>使用者所屬的全部部門（Entra 多群組使用者可能不只一個）。</summary>
    IReadOnlyList<string> Departments { get; }

    string Username { get; }
}
