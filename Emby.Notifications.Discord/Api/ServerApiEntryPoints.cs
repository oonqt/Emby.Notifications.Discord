using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using Emby.Notifications.Discord.Configuration;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;
using System.Net.Http;
using MediaBrowser.Controller.Configuration;

namespace Emby.Notifications.Discord.Api
{
    [Route("/Notification/Discord/Test/{UserID}", "POST", Summary = "Tests Discord")]
    public class TestNotification : IReturnVoid
    {
        [ApiMember(Name = "UserID", Description = "User Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string UserID { get; set; }
    }

    class ServerApiEndpoints : IService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly IJsonSerializer _jsonSerializer;

        public ServerApiEndpoints(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _httpClient = new HttpClient();
            _serverConfiguration = serverConfiguration;
            _jsonSerializer = jsonSerializer;
        }
        private DiscordOptions GetOptions(String userID)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, userID, StringComparison.OrdinalIgnoreCase));
        }

        public void Post(TestNotification request)
        {
            Task task = PostAsync(request);
            Task.WaitAll(task);
        }

        public async Task PostAsync(TestNotification request)
        {
            DiscordOptions options = GetOptions(request.UserID);

            DiscordMessage discordMessage = new DiscordMessage()
            {
                avatar_url = options.AvatarUrl,
                username = options.Username,
                embeds = new List<DiscordEmbed>()
                {
                    new DiscordEmbed()
                    {
                        color = int.Parse(options.EmbedColor.Substring(1, 6), System.Globalization.NumberStyles.HexNumber),
                        description = "This is a test notification from Emby",
                        title = "It worked!",
                        footer = new Footer
                        {
                            icon_url = options.AvatarUrl,
                            text = $"From {_serverConfiguration.Configuration.ServerName}"
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

            await DiscordWebhookHelper.ExecuteWebhook(discordMessage, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient);
        } 
    }
}
