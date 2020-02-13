using System;

namespace Emby.Notifications.Discord
{
    class DiscordEmbed
    {
        public int color { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public Footer footer { get; set; }
        public DateTime timestamp { get; set; }
    }

    class Footer
    {
        public string text { get; set; }
        public string icon_url { get; set; }
    }
}
