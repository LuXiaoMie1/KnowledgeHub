using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Auth;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Department =>
        accessor.HttpContext?.User.FindFirst("department")?.Value
        ?? throw new InvalidOperationException("缺少 department claim");
}
