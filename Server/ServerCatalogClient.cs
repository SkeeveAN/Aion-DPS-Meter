using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace AionSniffer.Server;

/// <summary>Fetches the curated server list from the community backend, for the "which server is
/// this character on" picker in Settings. Own HttpClient rather than reusing Upload/UploadClient's
/// -- that one's JsonSerializerOptions (WhenWritingNull) is specific to the upload payload shape
/// and would be an odd, unrelated coupling for a plain GET with no body.</summary>
public static class ServerCatalogClient
{
    private const string ApiBaseUrl = "https://dpsmeter.skeeve.tv";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // The backend returns plain camelCase JSON keys (id/name/version/kind, straight from a
    // Fastify route, no naming-policy layer of its own) - PropertyNameCaseInsensitive is required
    // here because System.Text.Json's actual default (unlike ASP.NET Core's Web defaults) is
    // case-SENSITIVE, which would otherwise silently deserialize every record to its default
    // values (Id 0, Name/Version/Kind null) instead of throwing something visibly wrong.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Empty list (never throws) on any network/server failure -- the Settings dialog must
    /// still open and work for everything else if the backend is briefly unreachable.</summary>
    public static async Task<List<ServerCatalogEntry>> FetchAsync()
    {
        try
        {
            var result = await Http.GetFromJsonAsync<List<ServerCatalogEntry>>(
                $"{ApiBaseUrl}/api/server-catalog", JsonOptions);
            return result ?? new List<ServerCatalogEntry>();
        }
        catch (Exception)
        {
            return new List<ServerCatalogEntry>();
        }
    }
}
