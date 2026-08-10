using System.Security.Claims;
using System.Text.Json;
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// NoDepartmentAuthorizationMiddlewareResultHandler：只在 403 是因為 DepartmentClaimRequirement
// 沒過時改寫 body，其他 403／401 原樣交給框架預設 handler，行為不變。
public class NoDepartmentAuthorizationMiddlewareResultHandlerTests
{
    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public bool ChallengeCalled;
        public bool ForbidCalled;
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            throw new NotImplementedException();
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            { ChallengeCalled = true; return Task.CompletedTask; }
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            { ForbidCalled = true; return Task.CompletedTask; }
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            throw new NotImplementedException();
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) =>
            throw new NotImplementedException();
    }

    private static readonly AuthorizationPolicy EmptySchemePolicy =
        new([new DepartmentClaimRequirement()], Array.Empty<string>());

    private static (HttpContext Context, FakeAuthenticationService Auth) NewContext()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var auth = new FakeAuthenticationService();
        var services = new ServiceCollection();
        services.AddSingleton<IAuthenticationService>(auth);
        context.RequestServices = services.BuildServiceProvider();
        return (context, auth);
    }

    [Fact]
    public async Task 因DepartmentClaimRequirement失敗_回403且body帶no_department()
    {
        var (context, auth) = NewContext();
        var result = PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed([new DepartmentClaimRequirement()]));

        await new NoDepartmentAuthorizationMiddlewareResultHandler()
            .HandleAsync(_ => Task.CompletedTask, context, EmptySchemePolicy, result);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(context.Response.Body);
        Assert.Equal("no_department", doc.RootElement.GetProperty("error").GetString());
        Assert.False(auth.ForbidCalled); // 我們自己寫 body，不再經過框架的 ForbidAsync
    }

    [Fact]
    public async Task 其他需求造成403_交給預設handler_不寫no_department_body()
    {
        var (context, auth) = NewContext();
        var result = PolicyAuthorizationResult.Forbid(
            AuthorizationFailure.Failed([new DenyAnonymousAuthorizationRequirement()]));

        await new NoDepartmentAuthorizationMiddlewareResultHandler()
            .HandleAsync(_ => Task.CompletedTask, context, EmptySchemePolicy, result);

        Assert.True(auth.ForbidCalled);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task 未帶合法token造成401Challenge_交給預設handler()
    {
        var (context, auth) = NewContext();
        var result = PolicyAuthorizationResult.Challenge();

        await new NoDepartmentAuthorizationMiddlewareResultHandler()
            .HandleAsync(_ => Task.CompletedTask, context, EmptySchemePolicy, result);

        Assert.True(auth.ChallengeCalled);
        Assert.Equal(0, context.Response.Body.Length);
    }
}
