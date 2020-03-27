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
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Configuration;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly ILibraryManager _libraryManager;
        private readonly HttpClient _httpClient;

        public List<Guid> queuedUpdateCheck = new List<Guid> { };

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _httpClient = new HttpClient();
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _libraryManager = libraryManager;

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");

            Thread metadataUpdateChecker = new Thread(new ThreadStart(CheckForMetadata));
            metadataUpdateChecker.Start();
        }

        private void CheckForMetadata()
        {
            do
            {
                queuedUpdateCheck.ForEach(itemId =>
                {
                    _logger.Debug("{0} queued for recheck", itemId.ToString());

                    BaseItem item = _libraryManager.GetItemById(itemId);

                    if (item.ProviderIds.Count > 0)
                    {
                        _logger.Debug("{0}[{1}] has metadata, sending notification", item.Id, item.Name);

                        DiscordOptions options = Plugin.Instance.Configuration.Options.FirstOrDefault(opt => opt.MediaAddedOverride == true);
                        ServerConfiguration serverConfig = _serverConfiguration.Configuration;

                        string serverName = options.ServerNameOverride ? serverConfig.ServerName : "Emby Server";
                        string LibraryType = item.GetType().Name;


                        // build primary info 
                        DiscordMessage mediaAddedEmbed = new DiscordMessage
                        {
                            username = options.Username,
                            avatar_url = options.AvatarUrl,
                            embeds = new List<DiscordEmbed>()
                            {
                                new DiscordEmbed()
                                {
                                    title = $"{item.Name} {(!String.IsNullOrEmpty(item.ProductionYear.ToString()) ? $"({item.ProductionYear.ToString()})" : "")} has been added to {serverName}",
                                    footer = new Footer
                                    {
                                        text = $"From {serverName}",
                                        icon_url = options.AvatarUrl
                                    },
                                    timestamp = DateTime.Now
                                }
                            },
                        };

                        if (!String.IsNullOrEmpty(item.Overview)) mediaAddedEmbed.embeds.First().description = item.Overview;
                        if (!String.IsNullOrEmpty(serverConfig.WanDdns)) mediaAddedEmbed.embeds.First().url = $"{(serverConfig.EnableHttps ? "https" : "http")}://{serverConfig.WanDdns}:{(serverConfig.EnableHttps ? serverConfig.PublicHttpsPort : serverConfig.PublicPort)}/web/index.html#!/item?id={itemId}&serverId={}";

                        // populate images
                        if (item.HasImage(ImageType.Primary))
                        {
                            mediaAddedEmbed.embeds.First().thumbnail = new Thumbnail
                            {
                                url = item.GetImagePath(ImageType.Primary)
                            };
                        }

                        // populate external URLs
                        List<Field> providerFields = new List<Field>();

                        item.ProviderIds.ToList().ForEach(provider =>
                        {
                            Field field = new Field
                            {
                                name = "External Details"
                            };

                            Boolean didPopulate = true;

                            _logger.Debug("{0} has provider {1} with providerid {2}", itemId, provider.Key, provider.Value);

                            // only adding imdb and tmdb for now until further testing
                            switch (provider.Key.ToLower())
                            {
                                case "imdb":
                                    field.value = $"[IMDb](https://www.imdb.com/title/{provider.Value}/)";
                                    break;
                                case "tmdb":
                                    field.value = $"[TMDb](https://www.themoviedb.org/{(LibraryType == "Movie" ? "movie" : "tv")}/{provider.Value})";
                                    break;
                                case "trakt":
                                    field.value = $"[Trakt](https://trakt.tv/{(LibraryType == "Movie" ? "movies" : "shows")}/{provider.Value})";
                                    break;
                                case "musicbrainztrack":
                                    field.value = $"[MusicBrainz Track](https://musicbrainz.org/track/{provider.Value})";
                                    break;
                                case "musicbrainzalbum":
                                    field.value = $"[MusicBrainz Album](https://musicbrainz.org/release/{provider.Value})";
                                    break;
                                case "theaudiodbalbum":
                                    field.value = $"[TADb Album](https://theaudiodb.com/album/{provider.Value})";
                                    break;
                                default:
                                    didPopulate = false;
                                    break;
                            }

                            if(didPopulate == true) providerFields.Add(field);
                        });

                        if (providerFields.Count() > 0) mediaAddedEmbed.embeds.First().fields = providerFields;

                        DiscordWebhookHelper.ExecuteWebhook(mediaAddedEmbed, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient).ConfigureAwait(false);
                        // after sending we want to remove this item from the list so it wont send the noti multiple times
                    }
                });

                Thread.Sleep(5000);
            } while (true);
        }

        private static string[] allowedMovieTypes = new string[] { "Movie", "Episode", "Audio" };

        private void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            BaseItem Item = changeEvent.Item;

            string LibraryType = Item.GetType().Name;
            _logger.Debug("{0} has type {1}", Item.Id, LibraryType); // REMOVE WHEN TESTING DONE \\

            // we will probably need to check for more here, im just trying to get it to work for now ( && Array.Exists(allowedMovieTypes, t => t == LibraryType) )
            if (!Item.IsVirtualItem) {
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
