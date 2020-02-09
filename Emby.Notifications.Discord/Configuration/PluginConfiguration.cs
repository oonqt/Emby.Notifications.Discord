﻿using System;
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

    public class DiscordOptions
    {
        public Boolean Enabled { get; set; }
        public String AvatarUrl { get; set; }
        public String Username { get; set; }
        public String DiscordWebhookURI { get; set; }
        public string MediaBrowserUserId { get; set; }
    }

}
