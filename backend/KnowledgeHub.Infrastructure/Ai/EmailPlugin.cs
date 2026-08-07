using System.ComponentModel;
using KnowledgeHub.Core.Entities;
using KnowledgeHub.Core.Interfaces;
using Microsoft.SemanticKernel;

namespace KnowledgeHub.Infrastructure.Ai;

public class EmailPlugin(IOutboxEmailRepository outbox)
{
    [KernelFunction("send_email")]
    [Description("寄送 email 通知給指定收件人")]
    public async Task<string> SendEmailAsync(
        [Description("收件人 email")] string to,
        [Description("主旨")] string subject,
        [Description("內文")] string body)
    {
        await outbox.AddAsync(new OutboxEmail
        {
            Id = Guid.NewGuid(), To = to, Subject = subject, Body = body,
            CreatedAtUtc = DateTime.UtcNow
        });
        return $"已寄出給 {to}。";
    }
}
