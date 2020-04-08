using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Emby.Notifications.Discord.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public DiscordOptions[] Options { get; set; }

        public PluginConfiguration()
        {
            Options = new DiscordOptions[] { };
        }
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
        public Boolean Enabled { get; set; }
        public Boolean ServerNameOverride { get; set; }
        public Boolean MediaAddedOverride { get; set; }
        public Boolean ExcludeExternalServerLinks { get; set; }

        public Boolean EnableMovies { get; set; }
        public Boolean EnableCollections { get; set; }
        public Boolean EnableEpisodes { get; set; }
        public Boolean EnableSeries { get; set; }
        public Boolean EnableSeasons { get; set; }
        public Boolean EnableAlbums { get; set; }
        public Boolean EnableSongs { get; set; }

        public String EmbedColor { get; set; }
        public String AvatarUrl { get; set; }
        public String Username { get; set; }
        public String DiscordWebhookURI { get; set; }
        public MentionTypes MentionType { get; set; }
    }
}
