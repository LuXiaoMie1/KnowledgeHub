using System.Security.Claims;
using Hangfire;
using KnowledgeHub.Api.Auth;
using KnowledgeHub.Api.Bot;
using KnowledgeHub.Core;
using KnowledgeHub.Core.Interfaces;
using KnowledgeHub.Infrastructure;
using KnowledgeHub.Infrastructure.Ai;
using KnowledgeHub.Infrastructure.Extraction;
using KnowledgeHub.Infrastructure.Jobs;
using KnowledgeHub.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.SemanticKernel;

var builder = WebApplication.CreateBuilder(args);
// appsettings.Local.json：本機／公司租戶的真實 Entra 值放這裡，不進版控（見 .gitignore、README）。
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<KnowledgeHubDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

var jwtKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("缺少 Jwt:SigningKey（user-secrets）");
var entraTenantId = builder.Configuration["Entra:TenantId"]
    ?? throw new InvalidOperationException("缺少 Entra:TenantId（appsettings.Local.json／user-secrets）");
var entraClientId = builder.Configuration["Entra:ClientId"]
    ?? throw new InvalidOperationException("缺少 Entra:ClientId（appsettings.Local.json／user-secrets）");
var entraGroupDepartmentMap = EntraGroupDepartmentMapper.LoadGroupDepartmentMap(builder.Configuration);

// 雙 authentication scheme 並存：本機開發沿用種子帳號的自簽 JWT（scheme "Bearer"），
// 公司帳號改用 Entra ID（scheme "Entra"）。預設 scheme 是個 policy scheme，只看
// token 的 issuer（不驗證簽章）決定轉給哪一個，[Authorize] 端點不用區分 token 來源。
builder.Services.AddAuthentication(EntraSchemeSelector.PolicySchemeName)
    .AddPolicyScheme(EntraSchemeSelector.PolicySchemeName, "依 token issuer 分流到 Entra 或既有自簽 JWT", o =>
        o.ForwardDefaultSelector = EntraSchemeSelector.Select)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o => JwtBearerConfigurator.Configure(
        o, jwtKey, builder.Configuration["Jwt:Issuer"], builder.Configuration["Jwt:Audience"]))
    .AddMicrosoftIdentityWebApi(
        _ => { },
        identityOptions =>
        {
            identityOptions.Instance = "https://login.microsoftonline.com/";
            identityOptions.TenantId = entraTenantId;
            identityOptions.ClientId = entraClientId;
        },
        jwtBearerScheme: EntraSchemeSelector.EntraSchemeName);
builder.Services.PostConfigure<JwtBearerOptions>(EntraSchemeSelector.EntraSchemeName, o =>
{
    // 同 JwtBearerConfigurator 的理由：關掉 inbound claim 改名，"groups" 與這裡另外
    // 補上的 "department" 才能保留字面 claim 名，CurrentUser 不用分辨 token 來源。
    // 實測確認過：AddMicrosoftIdentityWebApi 預設 MapInboundClaims=true，這行不是多餘的防呆。
    o.MapInboundClaims = false;
    // aud 依 API 端 manifest 的 accessTokenAcceptedVersion 而異：v1 token 是 api://{ClientId}、
    // v2 token 是裸 ClientId，兩種都要收（只收其一會在前端接上時整批 401）。用 PostConfigure
    // 確保晚於 AddMicrosoftIdentityWebApi 內部對 TokenValidationParameters 的設定執行，
    // 不會被蓋掉（PostConfigure 一律晚於同一個 named options 的所有 Configure 執行）。
    o.TokenValidationParameters.ValidAudiences = [entraClientId, $"api://{entraClientId}"];
    var previousOnTokenValidated = o.Events?.OnTokenValidated;
    o.Events ??= new JwtBearerEvents();
    o.Events.OnTokenValidated = async context =>
    {
        if (previousOnTokenValidated is not null) await previousOnTokenValidated(context);
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>().CreateLogger("EntraGroupDepartmentMapper");
        EntraGroupDepartmentMapper.ApplyDepartmentClaim(
            (ClaimsIdentity)context.Principal!.Identity!, entraGroupDepartmentMap, logger);
    };
});
builder.Services.AddAuthorization(options =>
{
    // 已通過驗證卻沒有部門（不在任何已映射安全性群組）的合法 token：預設 policy 要求
    // department claim，讓這類請求在 [Authorize] 端點統一回明確的 403（而非讓
    // CurrentUser.Department 丟例外變成 500）。DepartmentClaimRequirement／
    // NoDepartmentAuthorizationMiddlewareResultHandler 負責把這個 403 寫成可辨識的
    // no_department body，見兩者的類別註解。
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new DepartmentClaimRequirement())
        .Build();
});
builder.Services.AddSingleton<IAuthorizationHandler, DepartmentClaimHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, NoDepartmentAuthorizationMiddlewareResultHandler>();
builder.Services.AddHttpContextAccessor();
var seedUsers = builder.Configuration.GetSection("SeedUsers").Get<SeedUser[]>()
    ?? throw new InvalidOperationException("缺少 SeedUsers（appsettings.json）");
builder.Services.AddSingleton(seedUsers.AsEnumerable());
builder.Services.AddSingleton(new TokenService(jwtKey,
    builder.Configuration["Jwt:Issuer"]!, builder.Configuration["Jwt:Audience"]!));
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IDepartmentScope, CurrentUserDepartmentScope>();
builder.Services.AddScoped<IChunkRepository, ChunkRepository>();
builder.Services.AddScoped<IOutboxEmailRepository, OutboxEmailRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
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
// 取捨：每次 handler chain 重建（預設約 2 分鐘一次）都會重新讀金鑰檔＋建立新
// GoogleCredential，而不是整個 process 生命週期只讀一次；金鑰檔很小且只在本機讀取，
// 換來的是與既有 pipeline 一致的生命週期管理，判斷利大於弊。
builder.Services.AddTransient(_ => new GoogleOAuthHandler(vertexSaKeyPath));
builder.Services.AddHttpClient("vertex-embedding", c =>
    c.BaseAddress = new Uri("https://aiplatform.googleapis.com/"))
    .AddHttpMessageHandler<GoogleOAuthHandler>();
builder.Services.AddTransient<IEmbeddingService>(sp =>
    new GeminiEmbeddingService(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("vertex-embedding"),
        vertexProjectId, vertexLocation));
builder.Services.AddSingleton(new RetrievalOptions(
    builder.Configuration.GetValue("Retrieval:MaxDistance", 0.38)));
builder.Services.AddScoped<RetrievalPlugin>();
builder.Services.AddScoped<EmailPlugin>();
builder.Services.AddScoped<IChatService, SemanticKernelChatService>();
// GeminiThoughtSignatureHandler 不再掛進 pipeline：實測 Vertex 的 openapi 相容端點
// 多輪 function calling（search_knowledge_base、send_email）皆正常，不需要補
// thought_signature 佔位值（該問題只出現在 generativelanguage 端點，見該類別註解）。
builder.Services.AddHttpClient("gemini-chat")
    .AddHttpMessageHandler<GoogleOAuthHandler>();
var vertexChatEndpoint = new Uri(
    $"https://aiplatform.googleapis.com/v1beta1/projects/{vertexProjectId}/locations/{vertexLocation}/endpoints/openapi/");
builder.Services.AddScoped(sp =>
{
    var chatModel = builder.Configuration["Gemini:ChatModel"]
        ?? throw new InvalidOperationException("缺少 Gemini:ChatModel（appsettings.json）");
    return KernelFactory.Build(chatModel, vertexChatEndpoint,
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini-chat"),
        sp.GetRequiredService<RetrievalPlugin>(), sp.GetRequiredService<EmailPlugin>());
});

// Bot Framework：/api/messages 走 Bot Framework 自己的驗證機制（見下方端點的
// AllowAnonymous），不套用既有 JWT/Entra scheme。Bot:MicrosoftAppId 留空時
// ConfigurationBotFrameworkAuthentication 自動走匿名認證，本機用 Emulator
// 連線不需要任何憑證；之後要串 Teams，把 Bot:MicrosoftAppId/Password/TenantId
// 從 user-secrets 帶入正式值即可，不必改這裡的接線。
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BotFrameworkAuthentication>(
    _ => new ConfigurationBotFrameworkAuthentication(builder.Configuration.GetSection("Bot")));
// 顯式指定用哪個建構子：CloudAdapter 同時有 (IConfiguration, IHttpClientFactory, ILogger)
// 與 (BotFrameworkAuthentication, ILogger) 兩個建構子，DI 沒辦法自動判斷要用哪個
// （會丟 ambiguous constructors），改用 factory 明確走後者。
builder.Services.AddSingleton(sp =>
    new CloudAdapter(sp.GetRequiredService<BotFrameworkAuthentication>(), sp.GetRequiredService<ILogger<CloudAdapter>>()));
builder.Services.AddTransient<IBot, KnowledgeHubBotHandler>();
// bot 專用的 "bot" keyed 服務：與 web 端（/api/conversations/messages）完全獨立的一份 RetrievalPlugin／
// Kernel／IChatService，理由見 KnowledgeHubBotHandler 類別註解——
// 1) 部門範圍固定 AllDepartmentsScope（不經 ICurrentUser，匿名管道沒有 claim 可用）
// 2) kernel 不掛 EmailPlugin（email: null，匿名管道不可觸發寄信）
builder.Services.AddKeyedScoped<RetrievalPlugin>("bot", (sp, _) => new RetrievalPlugin(
    sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IChunkRepository>(),
    sp.GetRequiredService<RetrievalContext>(), new AllDepartmentsScope(),
    sp.GetRequiredService<RetrievalOptions>(), sp.GetRequiredService<ILogger<RetrievalPlugin>>()));
builder.Services.AddKeyedScoped<Kernel>("bot", (sp, _) =>
{
    var chatModel = builder.Configuration["Gemini:ChatModel"]
        ?? throw new InvalidOperationException("缺少 Gemini:ChatModel（appsettings.json）");
    return KernelFactory.Build(chatModel, vertexChatEndpoint,
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("gemini-chat"),
        sp.GetRequiredKeyedService<RetrievalPlugin>("bot"), email: null);
});
builder.Services.AddKeyedScoped<IChatService>("bot", (sp, _) =>
    new SemanticKernelChatService(sp.GetRequiredKeyedService<Kernel>("bot")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// /api/messages 豁免 HTTPS 轉址：Bot Framework Emulator 走 http 打進來，跟隨 307 到
// https 埠時不信任本機自簽憑證會直接失敗（同 lessons 2026-08-10 的 Authorization 案例）。
// 正式環境 bot 流量走 Azure Bot Service 的公開 https 端點，不受此豁免影響。
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/messages"),
    branch => branch.UseHttpsRedirection());

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Bot Framework 端點：不掛 [Authorize]，明確 AllowAnonymous 避免之後全域政策異動
// 誤把它納入驗證（Bot Framework 有自己的簽章驗證，見上方 BotFrameworkAuthentication 註冊）。
app.MapPost("/api/messages", async (
    HttpRequest request, HttpResponse response, CloudAdapter adapter, IBot bot, CancellationToken ct) =>
    await adapter.ProcessAsync(request, response, bot, ct))
    .AllowAnonymous();

app.Run();
