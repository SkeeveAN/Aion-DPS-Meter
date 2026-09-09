using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Posts one encounter. Returns false (never throws) on any network/server failure -
    /// a failed upload must not interrupt whatever the user is doing with a running boss fight.</summary>
    public static async Task<bool> SendAsync(EncounterUploadRequest payload)
    {
        try
        {
            using HttpResponseMessage response =
                await Http.PostAsJsonAsync($"{ApiBaseUrl}/api/uploads", payload, JsonOptions);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
