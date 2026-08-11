using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

/// <summary>
/// 讓前端在登入後查詢自己所屬的全部部門，用於上傳表單的部門選擇與畫面顯示
/// （多部門使用者無法再用單一 department claim 判斷）。
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public class MeController(ICurrentUser user) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { departments = user.Departments });
}
