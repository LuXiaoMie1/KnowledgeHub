using KnowledgeHub.Api.Controllers;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

// GET /api/me：前端用來判斷上傳表單要不要顯示部門下拉、以及部門顯示（多部門使用者
// 不能再用單一 department claim 判斷，見 CurrentUser 的類別註解）。
public class MeControllerTests
{
    private sealed class FakeUser(params string[] departments) : ICurrentUser
    {
        public string Department => departments is [var only] ? only
            : throw new InvalidOperationException("使用者屬於多個部門，無法使用單一部門語意");
        public IReadOnlyList<string> Departments => departments;
        public string Username => "it-user";
        public string UserKey => "test-user";
    }

    [Fact]
    public void 回傳使用者所屬全部部門()
    {
        var result = new MeController(new FakeUser("IT", "HR")).Get();

        var ok = Assert.IsType<OkObjectResult>(result);
        var departments = (IReadOnlyList<string>)ok.Value!.GetType().GetProperty("departments")!.GetValue(ok.Value)!;
        Assert.Equal(["IT", "HR"], departments);
    }
}
