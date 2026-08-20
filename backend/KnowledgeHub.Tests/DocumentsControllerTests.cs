using KnowledgeHub.Core;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text;

public class DocumentsControllerTests
{
    private sealed class FakeDocs : IDocumentRepository
    {
        public readonly List<CompanyDocument> Added = [];
        public Task AddAsync(CompanyDocument doc, CancellationToken ct = default)
            { Added.Add(doc); return Task.CompletedTask; }
        public Task<CompanyDocument?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Added.FirstOrDefault(d => d.Id == id));
        public Task<IReadOnlyList<CompanyDocument>> ListByDepartmentsAsync(IReadOnlyList<string> departments, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanyDocument>>(
                Added.Where(d => departments.Contains(d.Department) || d.Department == Departments.All).ToList());
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            { Added.RemoveAll(d => d.Id == id); return Task.CompletedTask; }
        public Task SaveChunksAndCompleteAsync(Guid docId, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task UpdateStatusAsync(Guid docId, DocumentStatus status, string? errorMessage = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeQueue : IDocumentJobQueue
    {
        public readonly List<Guid> Enqueued = [];
        public void Enqueue(Guid documentId) => Enqueued.Add(documentId);
    }

    private sealed class FakeUser(params string[] departments) : ICurrentUser
    {
        public string Department => departments is [var only] ? only
            : throw new InvalidOperationException("使用者屬於多個部門，無法使用單一部門語意");
        public IReadOnlyList<string> Departments => departments;
        public string Username => "it-user";
        public string UserKey => "test-user";
    }

    private static IFormFile File(string name, int sizeBytes)
    {
        var content = new byte[sizeBytes];
        return new FormFile(new MemoryStream(content), 0, sizeBytes, "file", name);
    }

    private static (DocumentsController Ctrl, FakeDocs Docs, FakeQueue Queue) NewController(
        string uploadRoot, params string[] userDepartments)
    {
        var docs = new FakeDocs();
        var queue = new FakeQueue();
        var user = userDepartments.Length == 0 ? new FakeUser("IT") : new FakeUser(userDepartments);
        var ctrl = new DocumentsController(docs, queue, user, new UploadOptions(uploadRoot));
        return (ctrl, docs, queue);
    }

    [Fact]
    public async Task 沒有檔案回400()
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath());
        var result = await ctrl.Upload(null, null, null);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("缺少檔案", badRequest.Value!.GetType().GetProperty("error")!.GetValue(badRequest.Value));
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Theory]
    [InlineData("report.docx")]
    [InlineData("script.exe")]
    public async Task 非PDF或MD回400(string fileName)
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath());
        var result = await ctrl.Upload(File(fileName, 100), null, null);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task 超過20MB回400()
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath());
        var result = await ctrl.Upload(File("big.pdf", 21 * 1024 * 1024), null, null);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task 合法上傳_建Pending_存檔_排入job_回202()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var (ctrl, docs, queue) = NewController(root);

        var result = await ctrl.Upload(File("sop.md", 100), null, null);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var doc = Assert.Single(docs.Added);
        Assert.Equal(DocumentStatus.Pending, doc.Status);
        Assert.Equal("IT", doc.Department);
        Assert.Equal("sop.md", doc.FileName);
        Assert.Equal([doc.Id], queue.Enqueued);
        Assert.True(System.IO.File.Exists(Path.Combine(root, $"{doc.Id}.md")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task scope為company_文件部門存為ALL()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var (ctrl, docs, _) = NewController(root);

        var result = await ctrl.Upload(File("all.md", 100), "company", null);

        Assert.IsType<AcceptedResult>(result);
        var doc = Assert.Single(docs.Added);
        Assert.Equal(Departments.All, doc.Department);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 單部門使用者_department_scope不帶department時用自己部門()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var (ctrl, docs, _) = NewController(root, "IT");

        var result = await ctrl.Upload(File("sop.md", 100), "department", null);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal("IT", Assert.Single(docs.Added).Department);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 多部門使用者_department_scope未帶department回400()
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath(), "IT", "HR");

        var result = await ctrl.Upload(File("sop.md", 100), "department", null);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task 多部門使用者_department帶非所屬部門回400()
    {
        var (ctrl, docs, queue) = NewController(Path.GetTempPath(), "IT", "HR");

        var result = await ctrl.Upload(File("sop.md", 100), "department", "Finance");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(docs.Added);
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task 多部門使用者_department帶所屬部門成功()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var (ctrl, docs, _) = NewController(root, "IT", "HR");

        var result = await ctrl.Upload(File("sop.md", 100), "department", "HR");

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal("HR", Assert.Single(docs.Added).Department);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 刪除他部門文件回404()
    {
        var (ctrl, docs, _) = NewController(Path.GetTempPath());
        var other = new CompanyDocument { Id = Guid.NewGuid(), Department = "HR", FileName = "hr.pdf" };
        docs.Added.Add(other);

        var result = await ctrl.Delete(other.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Contains(other, docs.Added); // 沒被刪
    }

    [Fact]
    public async Task List只回自己部門的文件()
    {
        var (ctrl, docs, _) = NewController(Path.GetTempPath());
        var itDoc1 = new CompanyDocument { Id = Guid.NewGuid(), Department = "IT", FileName = "it1.pdf" };
        var itDoc2 = new CompanyDocument { Id = Guid.NewGuid(), Department = "IT", FileName = "it2.md" };
        var hrDoc = new CompanyDocument { Id = Guid.NewGuid(), Department = "HR", FileName = "hr.pdf" };
        docs.Added.AddRange([itDoc1, itDoc2, hrDoc]);

        var result = await ctrl.List();

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        var ids = items.Select(i => (Guid)i.GetType().GetProperty("Id")!.GetValue(i)!).ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { itDoc1.Id, itDoc2.Id }, ids);
    }

    [Fact]
    public async Task 多部門使用者_List聯集回傳所屬各部門與全公司文件()
    {
        var (ctrl, docs, _) = NewController(Path.GetTempPath(), "IT", "HR");
        var itDoc = new CompanyDocument { Id = Guid.NewGuid(), Department = "IT", FileName = "it.pdf" };
        var hrDoc = new CompanyDocument { Id = Guid.NewGuid(), Department = "HR", FileName = "hr.pdf" };
        var finDoc = new CompanyDocument { Id = Guid.NewGuid(), Department = "Finance", FileName = "fin.pdf" };
        var allDoc = new CompanyDocument { Id = Guid.NewGuid(), Department = Departments.All, FileName = "all.pdf" };
        docs.Added.AddRange([itDoc, hrDoc, finDoc, allDoc]);

        var result = await ctrl.List();

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();
        var ids = items.Select(i => (Guid)i.GetType().GetProperty("Id")!.GetValue(i)!).ToList();
        Assert.Equal(new[] { itDoc.Id, hrDoc.Id, allDoc.Id }, ids); // Finance 排除，IT/HR/ALL 聯集
        var allItem = items.Single(i => (Guid)i.GetType().GetProperty("Id")!.GetValue(i)! == allDoc.Id);
        Assert.True((bool)allItem.GetType().GetProperty("IsCompanyWide")!.GetValue(allItem)!);
        var itItem = items.Single(i => (Guid)i.GetType().GetProperty("Id")!.GetValue(i)! == itDoc.Id);
        Assert.False((bool)itItem.GetType().GetProperty("IsCompanyWide")!.GetValue(itItem)!);
    }

    [Fact]
    public async Task 全公司文件_非文件所屬部門的使用者也能刪除()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var (ctrl, docs, _) = NewController(root, "HR");
        var doc = new CompanyDocument { Id = Guid.NewGuid(), Department = Departments.All, FileName = "all.md" };
        docs.Added.Add(doc);

        var result = await ctrl.Delete(doc.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.DoesNotContain(doc, docs.Added);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task 刪除同部門文件回204並清除檔案()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var (ctrl, docs, _) = NewController(root);
        var doc = new CompanyDocument { Id = Guid.NewGuid(), Department = "IT", FileName = "sop.md" };
        docs.Added.Add(doc);
        var filePath = Path.Combine(root, $"{doc.Id}.md");
        await System.IO.File.WriteAllTextAsync(filePath, "content");

        var result = await ctrl.Delete(doc.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.DoesNotContain(doc, docs.Added);
        Assert.False(System.IO.File.Exists(filePath));
        Directory.Delete(root, recursive: true);
    }
}
