using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// 包裝框架預設的 IAuthorizationMiddlewareResultHandler：只在 403 是因為
/// DepartmentClaimRequirement 沒過（已登入但沒有部門）時，改寫成可辨識的
/// JSON body，讓前端能區分「無部門」與其他 401/403。其他情況（未帶合法 token
/// 造成的 401 challenge、其他原因的 403）原樣交給預設 handler 處理，行為不變。
/// PolicyEvaluator 的既有邏輯：token 驗證本身失敗才會走 401 challenge，token
/// 合法但授權需求沒過一律是 403 forbid，所以「無部門」不會誤判成 401。
/// </summary>
public class NoDepartmentAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context,
        AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var noDepartment = authorizeResult.Forbidden
            && authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<DepartmentClaimRequirement>().Any() == true;

        if (!noDepartment)
        {
            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "no_department",
            message = "帳號尚未授權使用 KnowledgeHub，請聯絡資訊部"
        });
    }
}
