using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Emby.Notifications.Discord.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.Net.Http.Headers;
using MediaBrowser.Controller.Configuration;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly HttpClient _httpClient;

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _httpClient = new HttpClient();
        }

        public bool IsEnabledForUser(User user)
        {
            DiscordOptions options = GetOptions(user);

            return options != null && IsValid(options) && options.Enabled;
        }

        private DiscordOptions GetOptions(User user)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, user.Id.ToString("N"), StringComparison.OrdinalIgnoreCase));
        }

        public string Name
        {
            get { return Plugin.Instance.Name; }
        }

        public async Task SendNotification(UserNotification request, CancellationToken cancellationToken)
        {
            DiscordOptions options = GetOptions(request.User);

            DiscordMessage discordMessage = new DiscordMessage
            {
                avatar_url = options.AvatarUrl,
                username = options.Username,
                embeds = new List<DiscordEmbed>()
                {
                    new DiscordEmbed()
                    {
                        color = int.Parse(options.EmbedColor.Substring(1, 6), System.Globalization.NumberStyles.HexNumber),
                        title = request.Name,
                        description = request.Description,
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

            StringContent postData = new StringContent(_jsonSerializer.SerializeToString(discordMessage).ToString());

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, options.DiscordWebhookURI);
                req.Content = postData;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                await _httpClient.SendAsync(req).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                _logger.Error("Failed to make request to Discord: {0}", e);
            }
        }

        private bool IsValid(DiscordOptions options)
        {
            return !string.IsNullOrEmpty(options.DiscordWebhookURI);
        }
    }
}
