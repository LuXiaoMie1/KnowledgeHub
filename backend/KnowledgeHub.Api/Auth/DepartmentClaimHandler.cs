using Microsoft.AspNetCore.Authorization;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// DepartmentClaimRequirement 的處理器：使用者的 claims 裡有非空的 "department" 才算通過。
/// 不呼叫 context.Fail()——沒通過時保持 pending，框架自 .NET Core 3.0 起會把未 Succeed
/// 的 requirement 自動計入 FailedRequirements，NoDepartmentAuthorizationMiddlewareResultHandler
/// 靠這個判斷 403 的成因。
/// </summary>
public class DepartmentClaimHandler : AuthorizationHandler<DepartmentClaimRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, DepartmentClaimRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == "department" && !string.IsNullOrEmpty(c.Value)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
