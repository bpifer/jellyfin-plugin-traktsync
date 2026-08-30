using System.Globalization;
using Jellyfin.Plugin.TraktSync.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TraktSync;

/// <summary>
/// Jellyfin to Trakt one-way watched-state sync.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Server application paths.</param>
    /// <param name="xmlSerializer">Xml serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>Gets the singleton plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Trakt Sync";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b8f2a1c4-6d3e-4f7a-9c25-1e8b7d4a3f60");

    /// <inheritdoc />
    public override string Description => "One-way sync of watched movies and episodes from Jellyfin to Trakt.tv.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.Configuration.configPage.html",
                GetType().Namespace),
        };
    }
}
