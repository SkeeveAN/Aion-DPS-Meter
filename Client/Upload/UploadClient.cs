using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Upload;

/// <summary>
/// Sends an encounter to the community backend (aiondps.com). One static HttpClient for the
/// process's lifetime, per the usual .NET guidance (a fresh client per call exhausts sockets under
/// load) - not a concern here at this call volume, but free to get right.
/// </summary>
public static class UploadClient
{
    internal const string ApiBaseUrl = "https://aiondps.com";

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

    /// <summary>Set once the community server has said this client is too old to upload (HTTP 426, or the start-up check): from then on no
    /// upload is even tried, until the meter has been updated and restarted. Holds the version the server asks for.</summary>
    public static string? RequiredVersion { get; private set; }

    /// <summary>True while uploads are switched off because the client is outdated.</summary>
    public static bool IsBlocked => RequiredVersion is not null;

    /// <summary>Raised once when the client turns out to be outdated (with the version it should be). Not raised on the UI thread.</summary>
    public static event Action<string>? ClientOutdated;

    private static void Block(string requiredVersion)
    {
        bool first = RequiredVersion is null;
        RequiredVersion = string.IsNullOrWhiteSpace(requiredVersion) ? "?" : requiredVersion;
        if (first)
        {
            ClientOutdated?.Invoke(RequiredVersion);
        }
    }

    private static UploadResult BlockedResult() =>
        UploadResult.Failed("Old client version, please update (" + RequiredVersion + "). No uploads are possible until you do.");

    /// <summary>The server's answer to an upload: 426 Upgrade Required switches the uploads off. Returns the failure to report.</summary>
    private static async Task<UploadResult> FailureOf(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode == 426)
        {
            string required = "?";
            try
            {
                using JsonDocument doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("requiredVersion", out JsonElement v) && v.ValueKind == JsonValueKind.String)
                {
                    required = v.GetString() ?? "?";
                }
            }
            catch (JsonException)
            {
                // an answer we cannot read still means "update"
            }

            Block(required);
            return BlockedResult();
        }

        return UploadResult.Failed($"{(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(body, 200)}");
    }

    /// <summary>Asks the community server right at start-up whether this version may upload, so a client that is too old learns it at once
    /// and not with its first fight. A server that cannot be reached changes nothing (the upload itself is checked as well).</summary>
    public static async Task CheckPolicyAsync(string version)
    {
        try
        {
            using HttpClient quick = new() { Timeout = TimeSpan.FromSeconds(8) };
            using JsonDocument doc = JsonDocument.Parse(await quick.GetStringAsync($"{ApiBaseUrl}/api/client-policy?version={Uri.EscapeDataString(version)}"));
            if (doc.RootElement.TryGetProperty("allowed", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.False)
            {
                string required = doc.RootElement.TryGetProperty("requiredVersion", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "?" : "?";
                Block(required);
            }
        }
        catch (Exception)
        {
            // offline or an older server: nothing to decide here
        }
    }

    /// <summary>Posts one encounter. Never throws - on any network/server failure a failed upload
    /// must not interrupt whatever the user is doing with a running boss fight. <see cref="UploadResult.Error"/>
    /// carries the actual reason (an HTTP status/body, or the exception message) so the UI can show
    /// something more useful than a blanket "unreachable", which was misleading for e.g. a rejected
    /// payload - per the user, not knowing whether/why an upload failed was itself the problem.</summary>
    public static async Task<UploadResult> SendAsync(EncounterUploadRequest payload)
    {
        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            using HttpResponseMessage response =
                await Http.PostAsJsonAsync($"{ApiBaseUrl}/api/uploads", payload, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                return UploadResult.Ok;
            }

            return await FailureOf(response);
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    /// <summary>Posts Aion 2 character profiles that have no boss fight to attach to. Same
    /// never-throws contract as <see cref="SendAsync"/>.</summary>
    public static async Task<UploadResult> SendProfilesAsync(ProfilesUploadRequest payload)
    {
        if (IsBlocked)
        {
            return BlockedResult();
        }

        try
        {
            using HttpResponseMessage response =
                await Http.PostAsJsonAsync($"{ApiBaseUrl}/api/uploads/profiles", payload, JsonOptions);
            if (response.IsSuccessStatusCode)
            {
                return UploadResult.Ok;
            }

            return await FailureOf(response);
        }
        catch (Exception ex)
        {
            return UploadResult.Failed(ex.Message);
        }
    }

    private static string Truncate(string s, int maxLength) =>
        s.Length <= maxLength ? s : s[..maxLength] + "...";
}
