using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController(
    IDocumentRepository docs, IDocumentJobQueue queue,
    ICurrentUser user, UploadOptions upload) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".pdf", ".md"];
    private const long MaxBytes = 20 * 1024 * 1024;
    // 20–25MB 由本 controller 判斷回規格要求的 400；超過 25MB 由框架的 RequestSizeLimit
    // 擋成 413，屬防護性上限（避免 MaxBytes 附近誤差讓真實請求被框架搶先擋成 413 而非 400）。
    private const long FrameworkMaxBytes = 25 * 1024 * 1024;
    private const string ScopeDepartment = "department";
    private const string ScopeCompany = "company";

    [HttpPost]
    [RequestSizeLimit(FrameworkMaxBytes)]
    public async Task<IActionResult> Upload(
        IFormFile? file, [FromForm] string? scope, [FromForm] string? department, CancellationToken ct = default)
    {
        if (file is null)
            return BadRequest(new { error = "缺少檔案" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "只接受 .pdf 或 .md 檔案" });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = "檔案不可超過 20MB" });

        scope ??= ScopeDepartment;
        if (scope != ScopeDepartment && scope != ScopeCompany)
            return BadRequest(new { error = "scope 必須是 department 或 company" });

        string docDepartment;
        if (scope == ScopeCompany)
        {
            docDepartment = Departments.All;
        }
        else if (department is not null)
        {
            if (!user.Departments.Contains(department))
                return BadRequest(new { error = "department 必須是使用者所屬部門之一" });
            docDepartment = department;
        }
        else if (user.Departments.Count == 1)
        {
            docDepartment = user.Departments[0];
        }
        else
        {
            return BadRequest(new { error = "多部門使用者上傳時必須指定 department" });
        }

        var doc = new CompanyDocument
        {
            Id = Guid.NewGuid(), FileName = file.FileName,
            Department = docDepartment, Status = DocumentStatus.Pending,
            UploadedAtUtc = DateTime.UtcNow
        };
        Directory.CreateDirectory(upload.Root);
        var path = Path.Combine(upload.Root, $"{doc.Id}{ext}");
        await using (var fs = System.IO.File.Create(path))
            await file.CopyToAsync(fs, ct);

        await docs.AddAsync(doc, ct);
        queue.Enqueue(doc.Id);
        return Accepted(new { id = doc.Id });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var list = await docs.ListByDepartmentsAsync(user.Departments, ct);
        return Ok(list.Select(d => new
        {
            d.Id, d.FileName, Status = d.Status.ToString(),
            d.ChunkCount, d.ErrorMessage, d.UploadedAtUtc,
            IsCompanyWide = d.Department == Departments.All
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var doc = await docs.GetAsync(id, ct);
        // 全公司文件任何部門皆可刪（已知簡化，接受）。
        if (doc is null || (doc.Department != Departments.All && !user.Departments.Contains(doc.Department)))
            return NotFound();

        await docs.DeleteAsync(id, ct);
        foreach (var ext in AllowedExtensions)
        {
            var path = Path.Combine(upload.Root, $"{id}{ext}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        return NoContent();
    }
}
