using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// 檢索部門範圍的預設來源：委派給 <see cref="ICurrentUser"/>，用於一般有使用者身分的
/// 管道（web 端 /api/conversations/messages）。
/// </summary>
public class CurrentUserDepartmentScope(ICurrentUser user) : IDepartmentScope
{
    public IReadOnlyList<string> Departments => user.Departments;
}
