using System.Security.Claims;
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Authorization;

// DepartmentClaimHandler：已驗證使用者必須帶非空 "department" claim 才算通過，
// 對應「無部門帳號要回明確 403」規格的授權端邏輯。
public class DepartmentClaimHandlerTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "TestAuth"));

    private static async Task<bool> SucceedsAsync(ClaimsPrincipal user)
    {
        var context = new AuthorizationHandlerContext([new DepartmentClaimRequirement()], user, resource: null);
        await new DepartmentClaimHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task 有department_claim_通過()
    {
        Assert.True(await SucceedsAsync(Principal(new Claim("department", "IT"))));
    }

    [Fact]
    public async Task 沒有department_claim_不通過()
    {
        Assert.False(await SucceedsAsync(Principal(new Claim("sub", "user1"))));
    }

    [Fact]
    public async Task department_claim值為空字串_不通過()
    {
        Assert.False(await SucceedsAsync(Principal(new Claim("department", ""))));
    }
}
