using System;
using System.Collections.Generic;

namespace Emby.Notifications.Discord
{
    class DiscordEmbed
    {
        public int color { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public Thumbnail thumbnail { get; set; }
        public List<Field> fields { get; set; }
        public Footer footer { get; set; }
        public DateTime timestamp { get; set; }
    }

    class Thumbnail
    {
        public string url { get; set; }
    }

    class Field
    {
        public string name { get; set; }
        public string value { get; set; }
        public Boolean inline { get; set; }
    }

    class Footer
    {
        public string text { get; set; }
        public string icon_url { get; set; }
    }
}
