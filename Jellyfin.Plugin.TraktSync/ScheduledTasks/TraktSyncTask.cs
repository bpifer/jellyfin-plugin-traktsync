using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.TraktSync.Trakt;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TraktSync.ScheduledTasks;

/// <summary>
/// Pushes Jellyfin watched state to Trakt. One-way; nothing is ever removed
/// from Trakt and original watch timestamps are preserved.
/// </summary>
public class TraktSyncTask : IScheduledTask
{
    private const int HistoryPageLimit = 250;
    private const int HistoryMaxPages = 200;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TraktSyncTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraktSyncTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="userManager">User manager.</param>
    /// <param name="userDataManager">User data manager.</param>
    /// <param name="httpClientFactory">Http client factory.</param>
    /// <param name="logger">Logger.</param>
    public TraktSyncTask(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        IHttpClientFactory httpClientFactory,
        ILogger<TraktSyncTask> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync watched state to Trakt";

    /// <inheritdoc />
    public string Key => "TraktSyncPush";

    /// <inheritdoc />
    public string Description =>
        "Sends watched movies and episodes from Jellyfin to Trakt.tv, skipping anything already there.";

    /// <inheritdoc />
    public string Category => "Trakt";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Daily at 07:00, matching the cron schedule this replaces.
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(7).Ticks,
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance
            ?? throw new InvalidOperationException("Plugin instance is not available.");
        var config = plugin.Configuration;

        if (string.IsNullOrWhiteSpace(config.TraktClientId)
            || string.IsNullOrWhiteSpace(config.TraktClientSecret)
            || string.IsNullOrWhiteSpace(config.TraktRefreshToken))
        {
            _logger.LogWarning("Trakt is not configured; skipping sync.");
            return;
        }

        var http = _httpClientFactory.CreateClient(NamedClient.Default);
        var trakt = new TraktClient(
            http,
            _logger,
            config.TraktClientId,
            config.TraktClientSecret,
            config.TraktAccessToken,
            config.TraktRefreshToken,
            config.TraktTokenExpiresAt);

        // Refresh only near expiry so single-use refresh tokens are not wasted.
        // If the refresh succeeds but persisting fails, abort: the old refresh
        // token is already dead on Trakt's side and continuing would lose auth.
        if (trakt.NeedsRefresh)
        {
            _logger.LogInformation(
                "Trakt token expires in {Days:F0} days; refreshing",
                trakt.DaysUntilExpiry);

            await trakt.RefreshAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            config.TraktAccessToken = trakt.AccessToken;
            config.TraktRefreshToken = trakt.RefreshToken;
            config.TraktTokenExpiresAt = trakt.TokenExpiresAt;
            plugin.SaveConfiguration();

            _logger.LogInformation("Trakt token refreshed and saved.");
        }

        var user = ResolveUser(config.JellyfinUserId);
        if (user is null)
        {
            _logger.LogWarning("No Jellyfin user could be resolved; skipping sync.");
            return;
        }

        progress.Report(5);

        var excludeId = ParseGuid(config.ExcludeLibraryId);

        var moviePayload = await BuildMoviePayloadAsync(
            user, excludeId, trakt, cancellationToken).ConfigureAwait(false);
        progress.Report(40);

        var showPayload = await BuildShowPayloadAsync(
            user, excludeId, trakt, cancellationToken).ConfigureAwait(false);
        progress.Report(80);

        if (moviePayload.Count == 0 && showPayload.Count == 0)
        {
            _logger.LogInformation("Trakt is already up to date; nothing to send.");
            progress.Report(100);
            return;
        }

        if (config.DryRun)
        {
            _logger.LogInformation(
                "DRY RUN: would send {Movies} movies and {Shows} shows to Trakt.",
                moviePayload.Count,
                showPayload.Count);
            progress.Report(100);
            return;
        }

        await SubmitAsync(trakt, moviePayload, showPayload, config.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        progress.Report(100);
    }

    private static Guid? ParseGuid(string value)
        => Guid.TryParse(value, out var id) ? id : null;

    private static bool IsExcluded(BaseItem? item, Guid? excludeId)
    {
        if (excludeId is null || item is null)
        {
            return false;
        }

        return item.GetTopParent()?.Id == excludeId.Value;
    }

    /// <summary>
    /// Resolves the configured user. Deliberately does not fall back to an
    /// arbitrary account: guessing here would push someone else's watch history
    /// into the configured Trakt profile, which cannot be undone from Jellyfin.
    /// </summary>
    /// <param name="configuredId">User id from plugin configuration.</param>
    /// <returns>The user, or null when unset or unknown.</returns>
    private User? ResolveUser(string configuredId)
    {
        if (!Guid.TryParse(configuredId, out var id))
        {
            _logger.LogWarning(
                "No Jellyfin user configured. Set the user ID on the Trakt Sync settings page.");
            return null;
        }

        var match = _userManager.GetUserById(id);
        if (match is null)
        {
            _logger.LogWarning("Configured Jellyfin user {Id} was not found.", id);
        }

        return match;
    }

    private async Task<List<object>> BuildMoviePayloadAsync(
        User user,
        Guid? excludeId,
        TraktClient trakt,
        CancellationToken cancellationToken)
    {
        var watched = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            IsPlayed = true,
            Recursive = true,
        });

        _logger.LogInformation("{Count} watched movies in Jellyfin", watched.Count);

        var onTrakt = await trakt.GetWatchedMoviesAsync(cancellationToken).ConfigureAwait(false);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in onTrakt)
        {
            var ids = entry.Movie?.Ids;
            if (ids is null)
            {
                continue;
            }

            foreach (var key in TraktClient.ShowKeys(ids.Imdb, ids.Tmdb))
            {
                known.Add(key);
            }
        }

        _logger.LogInformation("{Count} movies already watched on Trakt", onTrakt.Count);

        var payload = new List<object>();

        foreach (var item in watched.OfType<Movie>())
        {
            if (IsExcluded(item, excludeId))
            {
                continue;
            }

            var imdb = item.GetProviderId(MetadataProvider.Imdb);
            var tmdbRaw = item.GetProviderId(MetadataProvider.Tmdb);
            int? tmdb = int.TryParse(tmdbRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
                ? t
                : null;

            if (string.IsNullOrEmpty(imdb) && tmdb is null)
            {
                continue;
            }

            if (TraktClient.ShowKeys(imdb, tmdb).Any(known.Contains))
            {
                continue;
            }

            var ids = new Dictionary<string, object>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(imdb))
            {
                ids["imdb"] = imdb;
            }

            if (tmdb is not null)
            {
                ids["tmdb"] = tmdb.Value;
            }

            var node = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["title"] = item.Name ?? string.Empty,
                ["ids"] = ids,
            };

            var watchedAt = _userDataManager.GetUserData(user, item)?.LastPlayedDate;
            if (watchedAt is not null)
            {
                node["watched_at"] = watchedAt.Value.ToUniversalTime()
                    .ToString("o", CultureInfo.InvariantCulture);
            }

            payload.Add(node);
        }

        _logger.LogInformation("{Count} movies to add", payload.Count);
        return payload;
    }

    private async Task<List<object>> BuildShowPayloadAsync(
        User user,
        Guid? excludeId,
        TraktClient trakt,
        CancellationToken cancellationToken)
    {
        var watched = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsPlayed = true,
            Recursive = true,
        });

        _logger.LogInformation("{Count} watched episodes in Jellyfin", watched.Count);

        var episodeMap = await trakt
            .GetWatchedEpisodeMapAsync(HistoryPageLimit, HistoryMaxPages, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "{Shows} show keys / {Episodes} episodes already watched on Trakt",
            episodeMap.Count,
            episodeMap.Sum(kv => kv.Value.Count));

        // showKey -> (ids, title, season -> episode nodes)
        var buckets = new Dictionary<string, ShowBucket>(StringComparer.Ordinal);
        var skipped = 0;

        foreach (var item in watched.OfType<Episode>())
        {
            var series = item.Series;
            if (series is null || IsExcluded(series, excludeId))
            {
                continue;
            }

            var seasonNumber = item.ParentIndexNumber;
            var episodeNumber = item.IndexNumber;
            if (seasonNumber is null || episodeNumber is null)
            {
                skipped++;
                continue;
            }

            var imdb = series.GetProviderId(MetadataProvider.Imdb);
            var tmdbRaw = series.GetProviderId(MetadataProvider.Tmdb);
            var tvdb = series.GetProviderId(MetadataProvider.Tvdb);
            int? tmdb = int.TryParse(tmdbRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
                ? t
                : null;

            string showKey;
            string title;
            int effectiveSeason;
            Dictionary<string, object> ids;

            var split = SplitOverrides.Resolve(tvdb, seasonNumber.Value);
            if (split is not null)
            {
                effectiveSeason = seasonNumber.Value + split.SeasonOffset;
                title = split.Title;
                ids = new Dictionary<string, object>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(split.Imdb))
                {
                    ids["imdb"] = split.Imdb;
                }

                if (split.Tmdb is not null)
                {
                    ids["tmdb"] = split.Tmdb.Value;
                }

                if (split.TraktId is not null)
                {
                    ids["trakt"] = split.TraktId.Value;
                }

                showKey = !string.IsNullOrEmpty(split.Imdb)
                    ? "imdb:" + split.Imdb
                    : split.Tmdb is not null
                        ? "tmdb:" + split.Tmdb.Value.ToString(CultureInfo.InvariantCulture)
                        : "trakt:" + split.TraktId!.Value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                if (string.IsNullOrEmpty(imdb) && tmdb is null)
                {
                    _logger.LogWarning(
                        "Skipping {Series}: no IMDB or TMDB id on the series",
                        series.Name);
                    skipped++;
                    continue;
                }

                effectiveSeason = seasonNumber.Value;
                title = series.Name ?? string.Empty;
                ids = new Dictionary<string, object>(StringComparer.Ordinal);
                if (!string.IsNullOrEmpty(imdb))
                {
                    ids["imdb"] = imdb;
                }

                if (tmdb is not null)
                {
                    ids["tmdb"] = tmdb.Value;
                }

                if (int.TryParse(tvdb, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tv))
                {
                    ids["tvdb"] = tv;
                }

                showKey = !string.IsNullOrEmpty(imdb)
                    ? "imdb:" + imdb
                    : "tmdb:" + tmdb!.Value.ToString(CultureInfo.InvariantCulture);
            }

            var code = TraktClient.FormatEpisodeCode(effectiveSeason, episodeNumber.Value);
            if (episodeMap.TryGetValue(showKey, out var seen) && seen.Contains(code))
            {
                continue;
            }

            if (!buckets.TryGetValue(showKey, out var bucket))
            {
                bucket = new ShowBucket(ids, title);
                buckets[showKey] = bucket;
            }

            var node = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["number"] = episodeNumber.Value,
            };

            var watchedAt = _userDataManager.GetUserData(user, item)?.LastPlayedDate;
            if (watchedAt is not null)
            {
                node["watched_at"] = watchedAt.Value.ToUniversalTime()
                    .ToString("o", CultureInfo.InvariantCulture);
            }

            if (!bucket.Seasons.TryGetValue(effectiveSeason, out var list))
            {
                list = new List<Dictionary<string, object>>();
                bucket.Seasons[effectiveSeason] = list;
            }

            list.Add(node);
        }

        var totalNew = buckets.Sum(b => b.Value.Seasons.Sum(s => s.Value.Count));
        _logger.LogInformation(
            "{Episodes} new episodes across {Shows} shows to add ({Skipped} skipped)",
            totalNew,
            buckets.Count,
            skipped);

        return buckets.Values.Select(b => (object)new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["title"] = b.Title,
            ["ids"] = b.Ids,
            ["seasons"] = b.Seasons
                .OrderBy(s => s.Key)
                .Select(s => new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["number"] = s.Key,
                    ["episodes"] = s.Value,
                })
                .ToList(),
        }).ToList();
    }

    private async Task SubmitAsync(
        TraktClient trakt,
        List<object> movies,
        List<object> shows,
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize < 1)
        {
            batchSize = 100;
        }

        if (movies.Count > 0)
        {
            var body = new Dictionary<string, object>(StringComparer.Ordinal) { ["movies"] = movies };
            var resp = await trakt.AddToHistoryAsync(body, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Trakt added {Count} movies", resp?.Added?.Movies ?? 0);
        }

        for (var i = 0; i < shows.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slice = shows.Skip(i).Take(batchSize).ToList();
            var body = new Dictionary<string, object>(StringComparer.Ordinal) { ["shows"] = slice };
            var resp = await trakt.AddToHistoryAsync(body, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Batch {Batch}: Trakt added {Count} episodes",
                (i / batchSize) + 1,
                resp?.Added?.Episodes ?? 0);
        }
    }

    private sealed class ShowBucket
    {
        public ShowBucket(Dictionary<string, object> ids, string title)
        {
            Ids = ids;
            Title = title;
            Seasons = new Dictionary<int, List<Dictionary<string, object>>>();
        }

        public Dictionary<string, object> Ids { get; }

        public string Title { get; }

        public Dictionary<int, List<Dictionary<string, object>>> Seasons { get; }
    }
}
