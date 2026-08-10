using Microsoft.AspNetCore.Authorization;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// 授權需求：已驗證的使用者必須帶有 "department" claim。獨立成具名 requirement
/// （而非直接用 RequireClaim），讓 NoDepartmentAuthorizationMiddlewareResultHandler
/// 能辨識 403 是否由這個需求造成，寫出可辨識的 no_department 錯誤 body。
/// </summary>
public class DepartmentClaimRequirement : IAuthorizationRequirement;
