using Newtonsoft.Json.Linq;
using NzbDrone.Common.Http;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using TidalSharp.Data;
using TidalSharp.Exceptions;

namespace TidalSharp;

public class API
{
    internal API(IHttpClient client, Session session)
    {
        _httpClient = client;
        _session = session;
    }

    private IHttpClient _httpClient;
    private Session _session;
    private TidalUser? _activeUser;

    public async Task<JObject> GetTrack(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"tracks/{id}", token: token);
    public async Task<TidalLyrics?> GetTrackLyrics(string id, CancellationToken token = default)
    {
        try
        {
            return (await Call(HttpMethod.Get, $"tracks/{id}/lyrics", token: token)).ToObject<TidalLyrics>()!;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    public async Task<JObject> GetAlbum(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"albums/{id}", token: token);
    public async Task<JObject> GetAlbumTracks(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"albums/{id}/tracks", token: token);

    public async Task<JObject> GetArtist(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"artists/{id}", token: token);
    public async Task<JObject> GetArtistAlbums(string id, FilterOptions filter = FilterOptions.ALL, CancellationToken token = default) => await Call(HttpMethod.Get, $"artists/{id}/albums",
        urlParameters: new()
        {
            { "filter", filter.ToString() }
        },
        token: token
    );

    public async Task<JObject> GetPlaylist(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"playlists/{id}", token: token);
    public async Task<JObject> GetPlaylistTracks(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"playlists/{id}/tracks", token: token);

    public async Task<JObject> GetVideo(string id, CancellationToken token = default) => await Call(HttpMethod.Get, $"videos/{id}", token: token);

    public async Task<JObject> GetMix(string id, CancellationToken token = default)
    {
        var result = await Call(HttpMethod.Get, "pages/mix",
            urlParameters: new()
            {
                { "mixId", id },
                { "deviceType", "BROWSER" }
            },
            token: token
        );

        var refactoredObj = new JObject()
        {
            { "mix", result["rows"]![0]!["modules"]![0]!["mix"] },
            { "tracks", result["rows"]![1]!["modules"]![0]!["pagedList"] }
        };

        return refactoredObj;
    }

    internal void UpdateUser(TidalUser user) => _activeUser = user;

    internal async Task<JObject> Call(
        HttpMethod method,
        string path,
        Dictionary<string, string>? formParameters = null,
        Dictionary<string, string>? urlParameters = null,
        Dictionary<string, string>? headers = null,
        string? baseUrl = null,
        CancellationToken token = default
    )
    {
        // currently the method is ignored, but that doesn't matter much since it's all GET

        baseUrl ??= Globals.API_V1_LOCATION;

        var request = _httpClient.BuildRequest(baseUrl).Resource(path);

        headers ??= [];
        urlParameters ??= [];
        urlParameters["sessionId"] = _activeUser?.SessionID ?? "";
        urlParameters["countryCode"] = _activeUser?.CountryCode ?? "";
        urlParameters["limit"] = _session.ItemLimit.ToString();

        if (_activeUser != null)
            headers["Authorization"] = $"{_activeUser.TokenType} {_activeUser.AccessToken}";

        foreach (var param in urlParameters)
            request = request.AddQueryParam(param.Key, param.Value, true);

        if (formParameters != null)
        {
            request = request.Post();
            foreach (var param in formParameters)
                request = request.AddFormParameter(param.Key, param.Value);
        }

        foreach (var header in headers)
            request = request.SetHeader(header.Key, header.Value);

        var response = await _httpClient.ProcessRequestAsync(request);

        // this is a side-precaution, in my testing it wouldn't happen assuming lidarr is properly rate limiting
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(Random.Shared.Next(100, 1000));
            return await Call(method, path, formParameters, urlParameters, headers, baseUrl, token);
        }

        string resp = response.Content;
        JObject json = JObject.Parse(resp);

        if (response.HasHttpError && !string.IsNullOrEmpty(_activeUser?.RefreshToken))
        {
            string? userMessage = json.GetValue("userMessage")?.ToString();
            if (userMessage != null && userMessage.Contains("The token has expired."))
            {
                bool refreshed = await _session.AttemptTokenRefresh(_activeUser, token);
                if (refreshed)
                {
                    await _activeUser.GetSession(this, token);
                    return await Call(method, path, formParameters, urlParameters, headers, baseUrl, token);
                }
            }
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            JToken? errors = json["errors"];
            if (errors != null && errors.Any())
                throw new ResourceNotFoundException(errors[0]!["detail"]!.ToString());

            JToken? userMessage = json["userMessage"];
            if (userMessage != null)
                throw new ResourceNotFoundException(userMessage.ToString());

            throw new ResourceNotFoundException(json.ToString());
        }

        if (response.HasHttpError)
        {
            JToken? errors = json["errors"];
            if (errors != null && errors.Any())
                throw new APIException(errors[0]!["detail"]!.ToString());

            JToken? userMessage = json["userMessage"];
            if (userMessage != null)
                throw new APIException(userMessage.ToString());

            throw new APIException(json.ToString());
        }

        return json;
    }

    public static string CompleteTitleFromPage(JToken page)
    {
        var title = page["title"]!.ToString();
        var version = page["version"]?.ToString();
        // we do the contains check as for whatever reason some albums (at least the one i looked at; 311544258) have the version already
        if (!string.IsNullOrEmpty(version) && !title.Contains(version, StringComparison.InvariantCulture))
            title = $"{title} ({version})";
        return title;
    }
}
