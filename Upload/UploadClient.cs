using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionSniffer.Upload;

/// <summary>
/// Sends an encounter to the community backend (dpsmeter.skeeve.tv). One static HttpClient for the
/// process's lifetime, per the usual .NET guidance (a fresh client per call exhausts sockets under
/// load) - not a concern here at this call volume, but free to get right.
/// </summary>
public static class UploadClient
{
    private const string ApiBaseUrl = "https://dpsmeter.skeeve.tv";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // WhenWritingNull is load-bearing, not cosmetic: the backend's zod schema marks optional fields
    // like serverName as .optional() (accepts a MISSING key) rather than .nullable() (accepts an
    // explicit null) - without this, a C# `null` (e.g. ServerName when nobody set a display name)
    // would serialize as a literal "null" in the JSON body and the whole upload would fail schema
    // validation, not just have that one field come through empty.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Posts one encounter. Never throws - on any network/server failure a failed upload
    /// must not interrupt whatever the user is doing with a running boss fight. <see cref="UploadResult.Error"/>
    /// carries the actual reason (an HTTP status/body, or the exception message) so the UI can show
    /// something more useful than a blanket "unreachable", which was misleading for e.g. a rejected
    /// payload - per the user, not knowing whether/why an upload failed was itself the problem.</summary>
    public static async Task<UploadResult> SendAsync(EncounterUploadRequest payload)
    {
        try
        {
            using HttpResponseMessage response =
                await Http.PostAsJsonAsync($"{ApiBaseUrl}/api/uploads", payload, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                return UploadResult.Ok;
            }

            string body = await response.Content.ReadAsStringAsync();
            return UploadResult.Failed($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 200)}");
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "...";
}
