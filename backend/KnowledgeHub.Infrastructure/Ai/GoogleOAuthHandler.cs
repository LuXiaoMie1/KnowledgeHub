using System.Net.Http.Headers;
using Google.Apis.Auth.OAuth2;

namespace KnowledgeHub.Infrastructure.Ai;

/// <summary>
/// Vertex AI（aiplatform.googleapis.com）走服務帳戶 OAuth，不是 API key。
/// 建構時用服務帳戶金鑰檔案建立具 cloud-platform scope 的憑證；每個請求前
/// 取一次 access token 蓋上 Authorization 標頭——GoogleCredential 本身有快取與到期更新，
/// 不必自己管理 token 存活時間。
/// </summary>
public class GoogleOAuthHandler : DelegatingHandler
{
    private readonly GoogleCredential _credential;

    public GoogleOAuthHandler(string saKeyPath)
    {
#pragma warning disable CS0618 // FromFile 已標記 Obsolete，但控制者指定的驗證路徑即此 API
        _credential = GoogleCredential.FromFile(saKeyPath)
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
#pragma warning restore CS0618
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}
