using System.Text;
using KnowledgeHub.Api.Auth;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Ai;
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
builder.Services.AddSingleton(builder.Configuration.GetSection("SeedUsers").Get<SeedUser[]>()!.AsEnumerable());
builder.Services.AddSingleton(new TokenService(jwtKey,
    builder.Configuration["Jwt:Issuer"]!, builder.Configuration["Jwt:Audience"]!));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IChunkRepository, ChunkRepository>();
builder.Services.AddScoped<IOutboxEmailRepository, OutboxEmailRepository>();
builder.Services.AddScoped<RetrievalContext>();
builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>(c =>
        c.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"))
    .AddTypedClient<IEmbeddingService>((http, sp) =>
        new GeminiEmbeddingService(http, builder.Configuration["Gemini:ApiKey"]!));
builder.Services.AddScoped<RetrievalPlugin>();
builder.Services.AddScoped<EmailPlugin>();
builder.Services.AddScoped<IChatService, SemanticKernelChatService>();
builder.Services.AddScoped(sp =>
{
    var chatModel = builder.Configuration["Gemini:ChatModel"]
        ?? throw new InvalidOperationException("缺少 Gemini:ChatModel（appsettings.json）");
    var kb = Kernel.CreateBuilder();
    kb.AddOpenAIChatCompletion(
        modelId: chatModel,
        endpoint: new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
        apiKey: builder.Configuration["Gemini:ApiKey"]!,
        // Gemini 的 OpenAI 相容端點在多輪 function calling 要求回填 thought_signature，
        // 而 SK 連接器不認得這個 Gemini 專屬欄位而遺失它，補一個 HttpClient handler 墊上佔位值。
        httpClient: new HttpClient(new GeminiThoughtSignatureHandler
        {
            InnerHandler = new HttpClientHandler()
        }));
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
