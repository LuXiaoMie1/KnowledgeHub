using System.Security.Claims;
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Http;

// CurrentUser：Departments 讀出全部 department claim（多部門聯集檢索的資料來源）；
// Department 單值屬性僅在恰有一個部門時可用，多部門時改用 Departments，避免誤用單一部門語意。
public class CurrentUserTests
{
    private static CurrentUser NewUser(params string[] departments)
    {
        var claims = departments.Select(d => new Claim("department", d))
            .Append(new Claim("sub", "it-user"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) };
        var accessor = new HttpContextAccessor { HttpContext = context };
        return new CurrentUser(accessor);
    }

    [Fact]
    public void 單一部門_Department與Departments皆可用()
    {
        var user = NewUser("IT");
        Assert.Equal("IT", user.Department);
        Assert.Equal(["IT"], user.Departments);
    }

    [Fact]
    public void 多個部門_Departments回傳全部()
    {
        var user = NewUser("IT", "HR");
        Assert.Equal(["IT", "HR"], user.Departments);
    }

    [Fact]
    public void 多個部門_Department丟例外()
    {
        var user = NewUser("IT", "HR");
        Assert.Throws<InvalidOperationException>(() => user.Department);
    }

    [Fact]
    public void 沒有department_claim_Departments丟例外()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "it-user")]))
        };
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = context });
        Assert.Throws<InvalidOperationException>(() => user.Departments);
    }
}
