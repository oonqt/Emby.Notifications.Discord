namespace Emby.Notifications.Discord
{
    internal class Constants
    {
        public static readonly int RecheckIntervalMS = 10000;
        public static readonly int MaxRetriesBeforeFallback = 10;
        public static readonly int MessageQueueSendInterval = 1000;
    }
}