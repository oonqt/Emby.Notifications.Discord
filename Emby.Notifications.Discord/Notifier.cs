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

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly HttpClient _httpClient;

        public Dictionary<Guid, int> queuedUpdateCheck = new Dictionary<Guid, int> { }; // k/v to store metadata retrieval attempts, if attempts fail at least x times then we check for individual metadatas or DDAMNIT WE NEED TO REWORK THIS

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager, IServerApplicationHost applicationHost)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _httpClient = new HttpClient();
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _libraryManager = libraryManager;
            _applicationHost = applicationHost;

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");

            Thread metadataUpdateChecker = new Thread(new ThreadStart(CheckForMetadata));
            metadataUpdateChecker.Start();
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

                    Boolean localMetadataFallback = queuedUpdateCheck[itemId] >= Constants.MaxRetriesBeforeFallback;

                    if (item.ProviderIds.Count > 0 || localMetadataFallback)
                    {
                        _logger.Debug("{0}[{1}] has metadata, sending notification", item.Id, item.Name);

                        DiscordOptions options = Plugin.Instance.Configuration.Options.FirstOrDefault(opt => opt.MediaAddedOverride == true);
                        PublicSystemInfo sysInfo = await _applicationHost.GetPublicSystemInfo(CancellationToken.None);
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

                        if (!String.IsNullOrEmpty(sysInfo.WanAddress)) mediaAddedEmbed.embeds.First().url = $"{sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={sysInfo.Id}";

                        // populate images causes issues w/ images that are local
                        if (item.HasImage(ImageType.Primary))
                        {
                            string imageUrl = "";

                            if(!item.GetImageInfo(ImageType.Primary, 0).IsLocalFile)
                            {
                                imageUrl = item.GetImagePath(ImageType.Primary);
                            } else if (serverConfig.EnableRemoteAccess == true)
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

                                _logger.Debug("{0} has provider {1} with providerid {2}", itemId, provider.Key, provider.Value);

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

                        await DiscordWebhookHelper.ExecuteWebhook(mediaAddedEmbed, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient);

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

            await DiscordWebhookHelper.ExecuteWebhook(discordMessage, options.DiscordWebhookURI, _jsonSerializer, _logger, _httpClient);
        }
    }
}
