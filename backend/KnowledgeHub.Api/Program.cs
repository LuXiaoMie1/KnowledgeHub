using System.Text;
using Hangfire;
using KnowledgeHub.Api.Auth;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Ai;
using KnowledgeHub.Infrastructure.Extraction;
using KnowledgeHub.Infrastructure.Jobs;
using KnowledgeHub.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<KnowledgeHubDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var jwtKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("缺少 Jwt:SigningKey（user-secrets）");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        // 保留 "department"/"sub" 原始 claim 名
        NameClaimType = "sub"
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
var seedUsers = builder.Configuration.GetSection("SeedUsers").Get<SeedUser[]>()
    ?? throw new InvalidOperationException("缺少 SeedUsers（appsettings.json）");
builder.Services.AddSingleton(seedUsers.AsEnumerable());
builder.Services.AddSingleton(new TokenService(jwtKey,
    builder.Configuration["Jwt:Issuer"]!, builder.Configuration["Jwt:Audience"]!));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IChunkRepository, ChunkRepository>();
builder.Services.AddScoped<IOutboxEmailRepository, OutboxEmailRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddSingleton(new UploadOptions(
    builder.Configuration["Upload:Root"] ?? "uploads"));
builder.Services.AddHangfire(c => c.UseSqlServerStorage(
    builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHangfireServer();
builder.Services.AddScoped<IDocumentJobQueue, HangfireDocumentJobQueue>();
builder.Services.AddScoped<DocumentProcessingJob>();
builder.Services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
builder.Services.AddScoped<IDocumentTextExtractor, MarkdownTextExtractor>();
builder.Services.AddScoped<RetrievalContext>();
var geminiApiKey = builder.Configuration["Gemini:ApiKey"]
    ?? throw new InvalidOperationException("缺少 Gemini:ApiKey（user-secrets）");
// 具名 HttpClient + AddTransient 手動組裝：AddHttpClient<TClient, TImpl> 產生的第一個 descriptor
// 無法解析 GeminiEmbeddingService 建構子的 string apiKey 參數，只靠後續註冊順序才能覆蓋掉、
// 靠順序活著的 DI 設定容易被後續改動悄悄弄壞。
builder.Services.AddHttpClient("gemini-embedding", c =>
    c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"));
builder.Services.AddTransient<IEmbeddingService>(sp =>
    new GeminiEmbeddingService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini-embedding"), geminiApiKey));
builder.Services.AddScoped<RetrievalPlugin>();
builder.Services.AddScoped<EmailPlugin>();
builder.Services.AddScoped<IChatService, SemanticKernelChatService>();
// Gemini 的 OpenAI 相容端點在多輪 function calling 要求回填 thought_signature，
// 而 SK 連接器不認得這個 Gemini 專屬欄位而遺失它，補一個 HttpClient handler 墊上佔位值。
// 走 IHttpClientFactory 具名用戶端，讓連線池（SocketsHttpHandler）在每個請求間共用，
// 避免 Kernel 這個 Scoped factory 每次請求都新建一個 HttpClient 造成連線池重建／socket 耗盡。
builder.Services.AddTransient<GeminiThoughtSignatureHandler>();
builder.Services.AddHttpClient("gemini-chat")
    .AddHttpMessageHandler<GeminiThoughtSignatureHandler>();
builder.Services.AddScoped(sp =>
{
    var chatModel = builder.Configuration["Gemini:ChatModel"]
        ?? throw new InvalidOperationException("缺少 Gemini:ChatModel（appsettings.json）");
    var kb = Kernel.CreateBuilder();
    kb.AddOpenAIChatCompletion(
        modelId: chatModel,
        endpoint: new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
        apiKey: geminiApiKey,
        httpClient: sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini-chat"));
    var kernel = kb.Build();
    kernel.Plugins.AddFromObject(sp.GetRequiredService<RetrievalPlugin>(), "retrieval");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<EmailPlugin>(), "email");
    return kernel;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
