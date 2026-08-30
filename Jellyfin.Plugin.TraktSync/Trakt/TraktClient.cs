using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TraktSync.Trakt;

/// <summary>
/// Minimal Trakt API client covering the endpoints this plugin needs.
/// </summary>
public class TraktClient
{
    private const string BaseUrl = "https://api.trakt.tv";

    /// <summary>
    /// Trakt access tokens last 90 days. Refresh only inside this window so a
    /// single-use refresh token is not burned on every run.
    /// </summary>
    private const int RefreshThresholdDays = 7;

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktClient"/> class.
    /// </summary>
    /// <param name="http">Http client.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="clientId">Trakt client id.</param>
    /// <param name="clientSecret">Trakt client secret.</param>
    /// <param name="accessToken">Current access token.</param>
    /// <param name="refreshToken">Current refresh token.</param>
    /// <param name="expiresAt">Unix timestamp of access token expiry.</param>
    public TraktClient(
        HttpClient http,
        ILogger logger,
        string clientId,
        string clientSecret,
        string accessToken,
        string refreshToken,
        long expiresAt)
    {
        _http = http;
        _logger = logger;
        _clientId = clientId;
        _clientSecret = clientSecret;
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        TokenExpiresAt = expiresAt;
    }

    /// <summary>Gets the current access token.</summary>
    public string AccessToken { get; private set; }

    /// <summary>Gets the current refresh token.</summary>
    public string RefreshToken { get; private set; }

    /// <summary>Gets the unix timestamp at which the access token expires.</summary>
    public long TokenExpiresAt { get; private set; }

    /// <summary>Gets the number of days until the access token expires.</summary>
    public double DaysUntilExpiry =>
        TokenExpiresAt == 0
            ? 0d
            : (TokenExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) / 86400d;

    /// <summary>Gets a value indicating whether the token should be refreshed now.</summary>
    public bool NeedsRefresh => DaysUntilExpiry < RefreshThresholdDays;

    /// <summary>Formats a season/episode pair as SxxEyy.</summary>
    /// <param name="season">Season number.</param>
    /// <param name="episode">Episode number.</param>
    /// <returns>The formatted code.</returns>
    public static string FormatEpisodeCode(int season, int episode)
        => string.Format(CultureInfo.InvariantCulture, "S{0:D2}E{1:D2}", season, episode);

    /// <summary>Yields the lookup keys for a show given its external ids.</summary>
    /// <param name="imdb">IMDB id.</param>
    /// <param name="tmdb">TMDB id.</param>
    /// <returns>Zero, one or two keys.</returns>
    public static IEnumerable<string> ShowKeys(string? imdb, int? tmdb)
    {
        if (!string.IsNullOrEmpty(imdb))
        {
            yield return "imdb:" + imdb;
        }

        if (tmdb.HasValue)
        {
            yield return "tmdb:" + tmdb.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Exchanges the stored refresh token for a new access token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the refresh.</returns>
    public async Task RefreshAccessTokenAsync(CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["refresh_token"] = RefreshToken,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["redirect_uri"] = "urn:ietf:wg:oauth:2.0:oob",
            ["grant_type"] = "refresh_token",
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/oauth/token")
        {
            Content = JsonContent.Create(body),
        };

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var token = await resp.Content
            .ReadFromJsonAsync<TraktTokenResponse>(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty token response from Trakt.");

        AccessToken = token.AccessToken;
        RefreshToken = token.RefreshToken;
        TokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            + (token.ExpiresIn > 0 ? token.ExpiresIn : 7776000);
    }

    /// <summary>
    /// Gets every movie already marked watched on Trakt.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The watched movies.</returns>
    public async Task<List<TraktWatchedMovie>> GetWatchedMoviesAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync<List<TraktWatchedMovie>>(
            HttpMethod.Get, "/sync/watched/movies?limit=1000", null, cancellationToken)
            .ConfigureAwait(false);

        return result ?? new List<TraktWatchedMovie>();
    }

    /// <summary>
    /// Builds a map of show key to the set of episode codes already on Trakt.
    /// </summary>
    /// <remarks>
    /// The obvious endpoint, /sync/watched/shows, does not return the
    /// seasons/episodes breakdown, so every episode set would come back empty
    /// and nothing would ever de-duplicate. Per-episode state is therefore
    /// derived from paginated watch history instead. Keys are emitted for both
    /// imdb and tmdb so either can be matched later.
    /// </remarks>
    /// <param name="pageLimit">Entries requested per page.</param>
    /// <param name="maxPages">Safety cap on pages fetched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Map of show key to episode codes.</returns>
    public async Task<Dictionary<string, HashSet<string>>> GetWatchedEpisodeMapAsync(
        int pageLimit,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        for (var page = 1; page <= maxPages; page++)
        {
            var path = string.Format(
                CultureInfo.InvariantCulture,
                "/sync/history/episodes?limit={0}&page={1}",
                pageLimit,
                page);

            var batch = await RequestAsync<List<TraktHistoryEntry>>(
                HttpMethod.Get, path, null, cancellationToken).ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                var season = entry.Episode?.Season;
                var number = entry.Episode?.Number;
                var ids = entry.Show?.Ids;

                if (season is null || number is null || ids is null)
                {
                    continue;
                }

                var code = FormatEpisodeCode(season.Value, number.Value);

                foreach (var key in ShowKeys(ids.Imdb, ids.Tmdb))
                {
                    if (!map.TryGetValue(key, out var set))
                    {
                        set = new HashSet<string>(StringComparer.Ordinal);
                        map[key] = set;
                    }

                    set.Add(code);
                }
            }

            if (batch.Count < pageLimit)
            {
                break;
            }
        }

        return map;
    }

    /// <summary>
    /// Posts watched items to Trakt history.
    /// </summary>
    /// <param name="payload">Body containing movies and/or shows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Trakt response.</returns>
    public Task<TraktSyncResponse?> AddToHistoryAsync(
        object payload,
        CancellationToken cancellationToken)
        => RequestAsync<TraktSyncResponse>(
            HttpMethod.Post, "/sync/history", payload, cancellationToken);

    private async Task<T?> RequestAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        const int MaxAttempts = 3;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var req = new HttpRequestMessage(method, BaseUrl + path);
            req.Headers.Add("trakt-api-key", _clientId);
            req.Headers.Add("trakt-api-version", "2");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

            if (body is not null)
            {
                req.Content = JsonContent.Create(body);
            }

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.NoContent)
            {
                return default;
            }

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                _logger.LogWarning(
                    "Trakt rate limit hit, waiting {Seconds}s",
                    wait.TotalSeconds);
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                continue;
            }

            resp.EnsureSuccessStatusCode();

            var json = await resp.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(json)
                ? default
                : JsonSerializer.Deserialize<T>(json);
        }

        throw new InvalidOperationException("Trakt rate limit not resolved after retries.");
    }
}
