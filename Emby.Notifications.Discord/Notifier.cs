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
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly HttpClient _httpClient;

        public List<Guid> queuedUpdateCheck;

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _httpClient = new HttpClient();

            libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");

            do {
                _logger.Debug("Simulate fake media check");

                Thread.Sleep(5000); // we check if metadata has been added every 30 seconds, more than enough
            } while (true);
        }

        public void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            BaseItem Item = changeEvent.Item;

            // we will probably need to check for more here, im just trying to get it to work for now
            if(!Item.IsVirtualItem) {
                queuedUpdateCheck.Add(Item.Id);
            }
        }

        public bool IsEnabledForUser(User user)
        {
            DiscordOptions options = GetOptions(user);

            return options != null && !string.IsNullOrEmpty(options.DiscordWebhookURI) && options.Enabled;
        }

        private DiscordOptions GetOptions(User user)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, user.Id.ToString("N"), StringComparison.OrdinalIgnoreCase));
        }

        public string Name => Plugin.Instance.Name;

        public async Task SendNotification(UserNotification request, CancellationToken cancellationToken)
        {
            DiscordOptions options = GetOptions(request.User);

            string serverName = _serverConfiguration.Configuration.ServerName;

            string footerText;
            string requestName;

            if (options.ServerNameOverride)
            {
                footerText = $"From {serverName}";
                requestName = request.Name.Replace("Emby Server", serverName);
            }
            else
            {
                requestName = request.Name;
                footerText = "From Emby Server";
            }

            DiscordMessage discordMessage = new DiscordMessage
            {
                avatar_url = options.AvatarUrl,
                username = options.Username,
                embeds = new List<DiscordEmbed>()
                {
                    new DiscordEmbed()
                    {
                        color = int.Parse(options.EmbedColor.Substring(1, 6), System.Globalization.NumberStyles.HexNumber),
                        title = requestName,
                        description = request.Description,
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

            await DiscordWebhookHelper.ExecuteWebhook(discordMessage, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient);
        }
    }
}
