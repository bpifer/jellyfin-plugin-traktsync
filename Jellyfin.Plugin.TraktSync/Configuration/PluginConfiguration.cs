using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TraktSync.Configuration;

/// <summary>
/// Plugin settings. Mirrors the config.json used by the original Python script.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the Trakt application client id.</summary>
    public string TraktClientId { get; set; } = string.Empty;

    /// <summary>Gets or sets the Trakt application client secret.</summary>
    public string TraktClientSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the current Trakt OAuth access token.</summary>
    public string TraktAccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets the Trakt OAuth refresh token.</summary>
    public string TraktRefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unix timestamp at which <see cref="TraktAccessToken"/> expires.
    /// Zero means unknown, which forces a refresh on the next run.
    /// </summary>
    public long TraktTokenExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets the Jellyfin user whose watched state is synced.
    /// Empty means "the first administrator account".
    /// </summary>
    public string JellyfinUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a library id to exclude from the sync (for example a YouTube
    /// library whose content does not exist on Trakt). Empty disables exclusion.
    /// </summary>
    public string ExcludeLibraryId { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the sync runs without POSTing to Trakt.</summary>
    public bool DryRun { get; set; }

    /// <summary>Gets or sets how many shows are submitted per Trakt request.</summary>
    public int BatchSize { get; set; } = 100;
}
