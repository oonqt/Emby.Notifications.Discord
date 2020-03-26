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
        public Boolean Enabled { get; set; }
        public Boolean ServerNameOverride { get; set; }
        public Boolean MediaAddedOverride { get; set; }
        public MentionTypes MentionType { get; set; }
        public String EmbedColor { get; set; }
        public String AvatarUrl { get; set; }
        public String Username { get; set; }
        public String DiscordWebhookURI { get; set; }
        public string MediaBrowserUserId { get; set; }
    }
}
