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
using MediaBrowser.Controller;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Library;
using System.IO;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly HttpClient _httpClient;

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _httpClient = new HttpClient();

            libraryManager.ItemUpdated += ItemUpdateHandler;
            libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Register item update/add handlers");
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            _logger.Debug("[ADD]: {0} ", changeEvent.Item.Name);

            switch (changeEvent.UpdateReason)
            {
                case ItemUpdateType.MetadataImport:
                    _logger.Debug("[ADD] Metadata imported for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.MetadataEdit:
                    _logger.Debug("[ADD] Metadata edited for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.ImageUpdate:
                    _logger.Debug("[ADD] Image updated for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.MetadataDownload:
                    _logger.Debug("[ADD] Metadata download for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.None:
                    _logger.Debug("[ADD] Update for no reason {0}", changeEvent.Item.Name);
                    break;
            }
        }

        public void ItemUpdateHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            _logger.Debug("[CHANGED]: {0}", changeEvent.Item.Name);

            switch(changeEvent.UpdateReason)
            {
                case ItemUpdateType.MetadataImport:
                    _logger.Debug("[UPDATE] Metadata imported for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.MetadataEdit:
                    _logger.Debug("[UPDATE] Metadata edited for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.ImageUpdate:
                    _logger.Debug("[UPDATE] Image updated for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.MetadataDownload:
                    _logger.Debug("[UPDATE] Metadata download for {0}", changeEvent.Item.Name);
                    break;
                case ItemUpdateType.None:
                    _logger.Debug("[UPDATE] Update for no reason {0}", changeEvent.Item.Name);
                    break;
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
