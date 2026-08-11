using System.ComponentModel;
using System.Net.Mail;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class EmailPlugin(IOutboxEmailRepository outbox, ICurrentUser user)
{
    [KernelFunction("send_email")]
    [Description("寄送 email 通知給指定收件人")]
    public async Task<string> SendEmailAsync(
        [Description("收件人 email")] string to,
        [Description("主旨")] string subject,
        [Description("內文")] string body)
    {
        if (!MailAddress.TryCreate(to, out _))
            return "收件者格式無效，請確認 email 地址正確。";

        await outbox.AddAsync(new OutboxEmail
        {
            Id = Guid.NewGuid(), To = to, Subject = subject, Body = body,
            CreatedAtUtc = DateTime.UtcNow,
            // 稽核用途的單一部門標記：多部門使用者用 user.Department 會丟例外（見其註解），
            // 這裡改用所屬部門的第一個做代表，不影響寄信本身的行為。
            Department = user.Departments[0], RequestedBy = user.Username
        });
        return $"已寄出給 {to}。";
    }
}
