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
    .AddJwtBearer(o =>
    {
        // JwtSecurityTokenHandler 預設會把 "sub" 這類標準 claim 自動改名成長版 URI
        // （ClaimTypes.NameIdentifier），導致 CurrentUser 用字面 "sub" 找不到值而丟例外；
        // 關掉這個舊行為，才能保留 "department"/"sub" 原始 claim 名。
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = "sub"
        };
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
// AI provider：Vertex AI（服務帳戶 OAuth），不再用 AI Studio 的 API key。
var vertexProjectId = builder.Configuration["Vertex:ProjectId"]
    ?? throw new InvalidOperationException("缺少 Vertex:ProjectId（appsettings.json）");
var vertexLocation = builder.Configuration["Vertex:Location"]
    ?? throw new InvalidOperationException("缺少 Vertex:Location（appsettings.json）");
var vertexSaKeyPath = builder.Configuration["Vertex:SaKeyPath"]
    ?? throw new InvalidOperationException("缺少 Vertex:SaKeyPath（user-secrets）");
if (!File.Exists(vertexSaKeyPath))
    throw new InvalidOperationException($"Vertex:SaKeyPath 指向的檔案不存在或無法讀取：{vertexSaKeyPath}");
// Transient：延續 GeminiThoughtSignatureHandler 的既有作法，讓具名 HttpClient 的連線池
// （SocketsHttpHandler）正常隨 IHttpClientFactory 週期重建，不把 handler 綁死成 singleton。
builder.Services.AddTransient(_ => new GoogleOAuthHandler(vertexSaKeyPath));
builder.Services.AddHttpClient("vertex-embedding", c =>
    c.BaseAddress = new Uri("https://aiplatform.googleapis.com/"))
    .AddHttpMessageHandler<GoogleOAuthHandler>();
builder.Services.AddTransient<IEmbeddingService>(sp =>
    new GeminiEmbeddingService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("vertex-embedding"),
        vertexProjectId, vertexLocation));
builder.Services.AddScoped<RetrievalPlugin>();
builder.Services.AddScoped<EmailPlugin>();
builder.Services.AddScoped<IChatService, SemanticKernelChatService>();
// GeminiThoughtSignatureHandler 不再掛進 pipeline：實測 Vertex 的 openapi 相容端點
// 多輪 function calling（search_knowledge_base、send_email）皆正常，不需要補
// thought_signature 佔位值（該問題只出現在 generativelanguage 端點，見該類別註解）。
builder.Services.AddHttpClient("gemini-chat")
    .AddHttpMessageHandler<GoogleOAuthHandler>();
builder.Services.AddScoped(sp =>
{
    var chatModel = builder.Configuration["Gemini:ChatModel"]
        ?? throw new InvalidOperationException("缺少 Gemini:ChatModel（appsettings.json）");
    var kb = Kernel.CreateBuilder();
    kb.AddOpenAIChatCompletion(
        modelId: chatModel,
        endpoint: new Uri($"https://aiplatform.googleapis.com/v1beta1/projects/{vertexProjectId}/locations/{vertexLocation}/endpoints/openapi/"),
        apiKey: "unused", // 真認證靠具名 HttpClient 上的 GoogleOAuthHandler，這裡 SK 連接器要求非空字串
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
