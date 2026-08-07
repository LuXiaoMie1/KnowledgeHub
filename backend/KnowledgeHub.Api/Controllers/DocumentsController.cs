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

    [HttpPost]
    [RequestSizeLimit(FrameworkMaxBytes)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "只接受 .pdf 或 .md 檔案" });
        if (file.Length > MaxBytes)
            return BadRequest(new { error = "檔案不可超過 20MB" });

        var doc = new CompanyDocument
        {
            Id = Guid.NewGuid(), FileName = file.FileName,
            Department = user.Department, Status = DocumentStatus.Pending,
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
        var list = await docs.ListByDepartmentAsync(user.Department, ct);
        return Ok(list.Select(d => new
        {
            d.Id, d.FileName, Status = d.Status.ToString(),
            d.ChunkCount, d.ErrorMessage, d.UploadedAtUtc
        }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var doc = await docs.GetAsync(id, ct);
        if (doc is null || doc.Department != user.Department) return NotFound();

        await docs.DeleteAsync(id, ct);
        foreach (var ext in AllowedExtensions)
        {
            var path = Path.Combine(upload.Root, $"{id}{ext}");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        return NoContent();
    }
}
