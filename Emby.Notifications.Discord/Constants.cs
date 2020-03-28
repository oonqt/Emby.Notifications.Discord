namespace Emby.Notifications.Discord
{
    class Constants
    {
        public static readonly string[] AllowedMediaTypes = new string[] { "Movie", "Episode", "Audio" };
        public static readonly int RecheckIntervalMS = 10000;
        public static readonly int MaxRetriesBeforeFallback = 10;
        public static readonly int MessageQueueSendInterval = 1000;
    }
}