namespace Jellyfin.Plugin.TraktSync;

/// <summary>
/// A rule remapping a range of TVDB seasons onto a different Trakt show.
/// </summary>
/// <param name="SeasonThreshold">First TVDB season number belonging to this Trakt show.</param>
/// <param name="SeasonOffset">Added to the TVDB season number to get the Trakt season.</param>
/// <param name="Imdb">Trakt show IMDB id, if known.</param>
/// <param name="Tmdb">Trakt show TMDB id, if known.</param>
/// <param name="TraktId">Trakt numeric id, if known.</param>
/// <param name="Title">Display title used in the Trakt payload and logs.</param>
public record SplitRule(
    int SeasonThreshold,
    int SeasonOffset,
    string? Imdb,
    int? Tmdb,
    int? TraktId,
    string Title);

/// <summary>
/// Some shows that Jellyfin/TVDB treat as one continuous series are split into
/// multiple separate Trakt entries. Rules are keyed by TVDB series id; the
/// highest matching season threshold wins.
/// </summary>
public static class SplitOverrides
{
    private static readonly Dictionary<string, List<SplitRule>> _rules = new(StringComparer.Ordinal)
    {
        // The Great British Bake Off.
        // Jellyfin/TVDB: one continuous series (tvdb:184871).
        // TVDB S8 is Channel 4 S1 on Trakt, hence offset -7.
        ["184871"] = new()
        {
            new SplitRule(8, -7, "tt21958588", 87012, 174953, "The Great British Bake Off"),
        },

        // Monster (Netflix anthology), tvdb:389492.
        // S3 Ed Gein is a separate Trakt show whose season 1 is TVDB season 3.
        ["389492"] = new()
        {
            new SplitRule(3, -2, null, 286801, 279819, "Monster: The Ed Gein Story"),
        },
    };

    /// <summary>
    /// Returns the matching split rule for a series/season, or null when none applies.
    /// </summary>
    /// <param name="tvdbId">TVDB series id.</param>
    /// <param name="seasonNumber">Season number as Jellyfin knows it.</param>
    /// <returns>The matching rule, or null.</returns>
    public static SplitRule? Resolve(string? tvdbId, int seasonNumber)
    {
        if (string.IsNullOrEmpty(tvdbId) || !_rules.TryGetValue(tvdbId, out var rules))
        {
            return null;
        }

        return rules
            .OrderByDescending(r => r.SeasonThreshold)
            .FirstOrDefault(r => seasonNumber >= r.SeasonThreshold);
    }
}
