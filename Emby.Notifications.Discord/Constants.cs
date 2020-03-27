namespace Emby.Notifications.Discord
{
    class Constants
    {
        public static readonly string[] AllowedMediaTypes = new string[] { "Movie", "Episode", "Audio" };
        public static readonly int RecheckIntervalMS = 5000;
        public static readonly int MaxRetriesBeforeFallback = 10;
    }
}
