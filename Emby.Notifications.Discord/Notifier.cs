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
using MediaBrowser.Controller;
using MediaBrowser.Model.System;
using MediaBrowser.Model.Globalization;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ILocalizationManager _localizationManager;
        private readonly HttpClient _httpClient;

        public Dictionary<Guid, int> queuedUpdateCheck = new Dictionary<Guid, int> { };
        public Dictionary<DiscordMessage, DiscordOptions> pendingSendQueue = new Dictionary<DiscordMessage, DiscordOptions> { };

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager, IServerApplicationHost applicationHost, ILocalizationManager localizationManager)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _httpClient = new HttpClient();
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _libraryManager = libraryManager;
            _localizationManager = localizationManager;
            _applicationHost = applicationHost;

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");

            Thread metadataUpdateChecker = new Thread(new ThreadStart(CheckForMetadata));
            Thread pendingMessageSender = new Thread(new ThreadStart(QueuedMessageSender));

            metadataUpdateChecker.Start();
            pendingMessageSender.Start();
        }

        private async void QueuedMessageSender()
        {
            do
            {
                if (pendingSendQueue.Count > 0)
                {
                    DiscordMessage messageToSend = pendingSendQueue.First().Key;
                    DiscordOptions options = pendingSendQueue.First().Value;

                    await DiscordWebhookHelper.ExecuteWebhook(messageToSend, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient);

                    pendingSendQueue.Remove(messageToSend);
                }

                Thread.Sleep(Constants.MessageQueueSendInterval);
            } while (true);
        }

        private void CheckForMetadata()
        {
            do
            {
                queuedUpdateCheck.ToList().ForEach(async updateCheck =>
                {
                    Guid itemId = updateCheck.Key;

                    _logger.Debug("{0} queued for recheck", itemId.ToString());

                    BaseItem item = _libraryManager.GetItemById(itemId);

                    LibraryOptions itemLibraryOptions = _libraryManager.GetLibraryOptions(item);
                    DiscordOptions options = Plugin.Instance.Configuration.Options.FirstOrDefault(opt => opt.MediaAddedOverride == true);
                    PublicSystemInfo sysInfo = await _applicationHost.GetPublicSystemInfo(CancellationToken.None);
                    ServerConfiguration serverConfig = _serverConfiguration.Configuration;

                    // for whatever reason if you have extraction on during library scans then it waits for the extraction to finish before populating the metadata.... I don't get why the fuck it goes in that order
                    // its basically impossible to make a prediction on how long it could take as its dependent on the bitrate, duration, codec, and processing power of the system
                    Boolean localMetadataFallback = queuedUpdateCheck[itemId] >= (itemLibraryOptions.ExtractChapterImagesDuringLibraryScan ? Constants.MaxRetriesBeforeFallback * 5.5 : Constants.MaxRetriesBeforeFallback);

                    if (item.ProviderIds.Count > 0 || localMetadataFallback)
                    {
                        _logger.Debug("{0}[{1}] has metadata, sending notification", item.Id, item.Name);

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
                                    color = DiscordWebhookHelper.FormatColorCode(options.EmbedColor),
                                    footer = new Footer
                                    {
                                        text = $"From {serverName}",
                                        icon_url = options.AvatarUrl
                                    },
                                    timestamp = DateTime.Now
                                }
                            },
                        };

                        if(LibraryType == "Audio")
                        {
                            List<BaseItem> artists = _libraryManager.GetAllArtists(item);

                            IEnumerable<string> artistsFormat = artists.Select(artist =>
                            {
                                if(artist.ProviderIds.Count() > 0)
                                {
                                    KeyValuePair<string, string> firstProvider = artist.ProviderIds.FirstOrDefault();

                                    string providerUrl = firstProvider.Key == "MusicBrainzArtist" ? $"https://musicbrainz.org/artist/{firstProvider.Value}" : $"https://theaudiodb.com/artist/{firstProvider.Value}";

                                    return $"[{artist.Name}]({providerUrl})";
                                } else
                                {
                                    return artist.Name;
                                }
                            });

                            if (artists.Count() > 0) mediaAddedEmbed.embeds.First().description = $"By {string.Join(", ", artistsFormat)}";
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(item.Overview)) mediaAddedEmbed.embeds.First().description = item.Overview; 
                        }

                        if (!String.IsNullOrEmpty(sysInfo.WanAddress) && !options.ExcludeExternalServerLinks) mediaAddedEmbed.embeds.First().url = $"{sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={sysInfo.Id}";

                        // populate images causes issues w/ images that are local
                        if (item.HasImage(ImageType.Primary))
                        {
                            string imageUrl = "";

                            if(!item.GetImageInfo(ImageType.Primary, 0).IsLocalFile)
                            {
                                imageUrl = item.GetImagePath(ImageType.Primary);
                            } else if (serverConfig.EnableRemoteAccess == true && !options.ExcludeExternalServerLinks) // in the future we can proxy images through memester server if people want to hide their server address
                            {
                                imageUrl = $"{sysInfo.WanAddress}/emby/Items/{itemId}/Images/Primary";
                            }

                            mediaAddedEmbed.embeds.First().thumbnail = new Thumbnail
                            {
                                url = imageUrl
                            };
                        }

                        // populate external URLs
                        List<Field> providerFields = new List<Field>();

                        if(!localMetadataFallback)
                        {
                            item.ProviderIds.ToList().ForEach(provider =>
                            {
                                Field field = new Field
                                {
                                    name = "External Details"
                                };

                                Boolean didPopulate = true;

                                switch (provider.Key.ToLower())
                                {
                                    case "imdb":
                                        field.value = $"[IMDb](https://www.imdb.com/title/{provider.Value}/)";
                                        break;
                                    case "tmdb":
                                        field.value = $"[TMDb](https://www.themoviedb.org/{(LibraryType == "Movie" ? "movie" : "tv")}/{provider.Value})";
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

                                if (didPopulate == true) providerFields.Add(field);
                            });

                            if (providerFields.Count() > 0) mediaAddedEmbed.embeds.First().fields = providerFields;
                        }

                        pendingSendQueue.Add(mediaAddedEmbed, options);

                        queuedUpdateCheck.Remove(itemId);
                    } else
                    {
                        queuedUpdateCheck[itemId]++;

                        _logger.Debug("Attempt: {0}", queuedUpdateCheck[itemId]);
                    }
                });

                Thread.Sleep(Constants.RecheckIntervalMS);
            } while (true);
        }


        private void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            BaseItem Item = changeEvent.Item;
            DiscordOptions options = Plugin.Instance.Configuration.Options.FirstOrDefault(opt => opt.MediaAddedOverride == true);

            string LibraryType = Item.GetType().Name;

            if (!Item.IsVirtualItem && Array.Exists(Constants.AllowedMediaTypes, t => t == LibraryType) && options != null) {
                queuedUpdateCheck.Add(Item.Id, 0);
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
            await Task.Run(() =>
            {
                DiscordOptions options = GetOptions(request.User);

                if (options.MediaAddedOverride && !request.Name.Contains(_localizationManager.GetLocalizedString("ValueHasBeenAddedToLibrary").Replace("{0} ", "").Replace(" {1}", "")) || !options.MediaAddedOverride)
                {
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
                                color = DiscordWebhookHelper.FormatColorCode(options.EmbedColor),
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

                    pendingSendQueue.Add(discordMessage, options);
                }
            });
        }
    }
}
