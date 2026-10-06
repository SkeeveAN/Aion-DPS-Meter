using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Upload;

namespace AionDPS.Feedback;

public sealed record FeedbackRequest(string Type, string Name, string? Email, string Message, string Source, string? ClientVersion, string? Lang, string? Recording);

/// <summary>Sends a bug report / suggestion to aiondps.com, which turns it into a GitHub issue. The
/// client holds no GitHub credentials; the server does. Never throws, like <see cref="UploadClient"/>.</summary>
public static class FeedbackClient
{
    // A recording can be several MB; the upload client's 15 s would cut it off on a slow line.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<UploadResult> SendAsync(FeedbackRequest request)
    {
        try
        {
            using HttpResponseMessage response = await Http.PostAsJsonAsync("https://aiondps.com/api/feedback", request, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                return UploadResult.Ok;
            }

            string body = await response.Content.ReadAsStringAsync();
            return UploadResult.Failed($"{(int)response.StatusCode} {response.ReasonPhrase}: {(body.Length <= 200 ? body : body[..200] + "...")}");
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }
}
