using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Bot;

/// <summary>
/// 固定回傳全公司共用文件範圍（<see cref="KnowledgeHub.Core.Departments.All"/>）。bot 走
/// Bot Framework 匿名/自家驗證，沒有使用者身分與部門 claim，檢索絕不可查到部門限定文件
/// ——見 KnowledgeHubBotHandler 類別註解與 Program.cs 的 "bot" keyed 服務註冊。
/// </summary>
public class AllDepartmentsScope : IDepartmentScope
{
    // 直接寫死全限定名稱：類別內已有同名的 Departments 屬性，若加 using KnowledgeHub.Core
    // 會與此屬性名稱衝突，導致 Departments.All 被誤判成存取屬性本身。
    public IReadOnlyList<string> Departments { get; } = [KnowledgeHub.Core.Departments.All];
}
