using System.Collections.Generic;

namespace Emby.Notifications.Discord
{
    class DiscordMessage
    {
        public string avatar_url { get; set; }
        public string username { get; set; }
        public string content { get; set; }
        public List<DiscordEmbed> embeds { get; set; }
    }
}
