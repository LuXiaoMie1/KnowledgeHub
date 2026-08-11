namespace KnowledgeHub.Core;

/// <summary>
/// 部門相關常數。<see cref="All"/> 代表「全公司共用」文件——沿用既有 CompanyDocument.Department
/// 欄位存放這個特殊值，不新增資料表欄位、不需要 migration。
/// </summary>
public static class Departments
{
    public const string All = "ALL";
}
