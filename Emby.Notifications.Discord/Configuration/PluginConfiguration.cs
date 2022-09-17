using MediaBrowser.Model.Plugins;

namespace Emby.Notifications.Discord.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            Options = new DiscordOptions[] { };
        }

        public DiscordOptions[] Options { get; set; }
    }

    public enum MentionTypes
    {
        Everyone = 2,
        Here = 1,
        None = 0
    }

    public class DiscordOptions
    {
        public string MediaBrowserUserId { get; set; }
        public bool Enabled { get; set; }
        public bool ServerNameOverride { get; set; }
        public bool MediaAddedOverride { get; set; }
        public bool ExcludeExternalServerLinks { get; set; }

        public bool EnableMovies { get; set; }
        public bool EnableEpisodes { get; set; }
        public bool EnableSeries { get; set; }
        public bool EnableSeasons { get; set; }
        public bool EnableAlbums { get; set; }
        public bool EnableSongs { get; set; }

        public string EmbedColor { get; set; }
        public string AvatarUrl { get; set; }
        public string Username { get; set; }
        public string DiscordWebhookURI { get; set; }
        public MentionTypes MentionType { get; set; }
    }
}