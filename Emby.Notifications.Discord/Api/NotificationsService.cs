using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Emby.Notifications.Discord.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

namespace Emby.Notifications.Discord.Api
{
    [Route("/Notifications/Discord/Test/{UserID}", "POST", Summary = "Tests Discord")]
    [Authenticated(Roles = "Admin")]
    public class TestNotification : IReturnVoid
    {
        [ApiMember(Name = "UserID", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string UserID { get; set; }
    }

    internal class NotificationsService : IService
    {
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _serverConfiguration;

        public NotificationsService(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _serverConfiguration = serverConfiguration;
            _jsonSerializer = jsonSerializer;
        }

        private DiscordOptions GetOptions(string userID)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, userID, StringComparison.OrdinalIgnoreCase));
        }

        public void Post(TestNotification request)
        {
            var task = PostAsync(request);
            Task.WaitAll(task);
        }

        public async Task PostAsync(TestNotification request)
        {
            var options = GetOptions(request.UserID);

            string footerText;

            if (options.ServerNameOverride)
                footerText = $"From {_serverConfiguration.Configuration.ServerName}";
            else
                footerText = "From Emby Server";

            var discordMessage = new DiscordMessage
            {
                avatar_url = options.AvatarUrl,
                username = options.Username,
                embeds = new List<DiscordEmbed>
                {
                    new DiscordEmbed
                    {
                        color = int.Parse(options.EmbedColor.Substring(1, 6), NumberStyles.HexNumber),
                        description = "This is a test notification from Emby",
                        title = "It worked!",
                        footer = new Footer
                        {
                            icon_url = options.AvatarUrl,
                            text = footerText
                        },
                        timestamp = DateTime.Now
                    }
                }
            };

            switch (options.MentionType)
            {
                case MentionTypes.Everyone:
                    discordMessage.content = "@everyone";
                    break;
                case MentionTypes.Here:
                    discordMessage.content = "@here";
                    break;
            }

            try
            {
                await DiscordWebhookHelper.ExecuteWebhook(discordMessage, options.DiscordWebhookURI, _jsonSerializer);
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to execute webhook", e);
                throw new ArgumentException();
            }
        }
    }
}