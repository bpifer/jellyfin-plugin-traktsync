using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TraktSync.Trakt;

/// <summary>Trakt id bag. Only the fields this plugin uses are modelled.</summary>
public class TraktIds
{
    /// <summary>Gets or sets the IMDB id.</summary>
    [JsonPropertyName("imdb")]
    public string? Imdb { get; set; }

    /// <summary>Gets or sets the TMDB id.</summary>
    [JsonPropertyName("tmdb")]
    public int? Tmdb { get; set; }

    /// <summary>Gets or sets the TVDB id.</summary>
    [JsonPropertyName("tvdb")]
    public int? Tvdb { get; set; }

    /// <summary>Gets or sets the Trakt numeric id.</summary>
    [JsonPropertyName("trakt")]
    public int? Trakt { get; set; }
}

/// <summary>A show or movie reference carrying ids.</summary>
public class TraktMediaRef
{
    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the ids.</summary>
    [JsonPropertyName("ids")]
    public TraktIds? Ids { get; set; }
}

/// <summary>One entry from /sync/watched/movies.</summary>
public class TraktWatchedMovie
{
    /// <summary>Gets or sets the movie.</summary>
    [JsonPropertyName("movie")]
    public TraktMediaRef? Movie { get; set; }
}

/// <summary>Episode reference inside a history entry.</summary>
public class TraktHistoryEpisode
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    /// <summary>Gets or sets the episode number.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }
}

/// <summary>One entry from /sync/history/episodes.</summary>
public class TraktHistoryEntry
{
    /// <summary>Gets or sets the episode.</summary>
    [JsonPropertyName("episode")]
    public TraktHistoryEpisode? Episode { get; set; }

    /// <summary>Gets or sets the show the episode belongs to.</summary>
    [JsonPropertyName("show")]
    public TraktMediaRef? Show { get; set; }
}

/// <summary>OAuth token response.</summary>
public class TraktTokenResponse
{
    /// <summary>Gets or sets the access token.</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the refresh token.</summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the lifetime in seconds.</summary>
    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }
}

/// <summary>Counts returned by /sync/history.</summary>
public class TraktSyncCounts
{
    /// <summary>Gets or sets the movie count.</summary>
    [JsonPropertyName("movies")]
    public int Movies { get; set; }

    /// <summary>Gets or sets the episode count.</summary>
    [JsonPropertyName("episodes")]
    public int Episodes { get; set; }
}

/// <summary>Response body of /sync/history.</summary>
public class TraktSyncResponse
{
    /// <summary>Gets or sets the added counts.</summary>
    [JsonPropertyName("added")]
    public TraktSyncCounts? Added { get; set; }

    /// <summary>Gets or sets the updated counts.</summary>
    [JsonPropertyName("updated")]
    public TraktSyncCounts? Updated { get; set; }
}
