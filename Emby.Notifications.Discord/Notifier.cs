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
using MediaBrowser.Model.Library;
using MediaBrowser.Model.IO;
using System.IO;
using System.Timers;

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
        private readonly IUserViewManager _userViewManager;
        private readonly IUserManager _userManager;
        private readonly IFileSystem _fileSystem;


        public Dictionary<Guid, int> queuedUpdateCheck = new Dictionary<Guid, int> { };
        public Dictionary<DiscordMessage, DiscordOptions> pendingSendQueue = new Dictionary<DiscordMessage, DiscordOptions> { };

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager, IServerApplicationHost applicationHost, ILocalizationManager localizationManager, IUserViewManager userViewManager, IUserManager userManager, IFileSystem fileSystem)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _httpClient = new HttpClient();
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _libraryManager = libraryManager;
            _localizationManager = localizationManager;
            _applicationHost = applicationHost;
            _userViewManager = userViewManager;
            _userManager = userManager;
            _fileSystem = fileSystem;

            System.Timers.Timer QueuedMessageHandler = new System.Timers.Timer(Constants.MessageQueueSendInterval);
            QueuedMessageHandler.AutoReset = true;
            QueuedMessageHandler.Elapsed += QueuedMessageSender;
            QueuedMessageHandler.Start();

            System.Timers.Timer QueuedUpdateHandler = new System.Timers.Timer(Constants.RecheckIntervalMS);
            QueuedUpdateHandler.AutoReset = true;
            QueuedUpdateHandler.Elapsed += CheckForMetadata;
            QueuedUpdateHandler.Start();

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");
        }

        private async void QueuedMessageSender(object sender, ElapsedEventArgs eventArgs)
        {
            try
            {
                if (pendingSendQueue.Count > 0)
                {
                    DiscordMessage messageToSend = pendingSendQueue.First().Key;
                    DiscordOptions options = pendingSendQueue.First().Value;

                    await DiscordWebhookHelper.ExecuteWebhook(messageToSend, options.DiscordWebhookURI, _jsonSerializer, _httpClient);

                    pendingSendQueue.Remove(messageToSend);
                }   
            }
            catch(Exception e)
            {
                _logger.ErrorException("Failed to execute webhook: ", e);
            }
        }

        private void CheckForMetadata(object sender, ElapsedEventArgs eventArgs)
        {
            try
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

                    if (!isInVisibleLibrary(options.MediaBrowserUserId, item))
                    {
                        queuedUpdateCheck.Remove(itemId);
                        _logger.Debug("User does not have access to library, skipping...");
                        return;
                    }

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

                        // populate title

                        string titleText;

                        if (LibraryType == "Episode")
                        {
                            titleText = $"{item.Parent.Parent.Name} {(item.ParentIndexNumber != null ? $"S{formatIndex(item.ParentIndexNumber)}" : "")}{(item.IndexNumber != null ? $"E{formatIndex(item.IndexNumber)}" : "")} {item.Name}";
                        }
                        else
                        {
                            titleText = $"{item.Name} {(!String.IsNullOrEmpty(item.ProductionYear.ToString()) ? $"({item.ProductionYear.ToString()})" : "")}";
                        }

                        mediaAddedEmbed.embeds.First().title = _localizationManager.GetLocalizedString("ValueHasBeenAddedToLibrary").Replace("{0}", titleText).Replace("{1}", serverName);

                        // populate description
                        if (LibraryType == "Audio")
                        {
                            List<BaseItem> artists = _libraryManager.GetAllArtists(item);

                            IEnumerable<string> artistsFormat = artists.Select(artist =>
                            {
                                if (artist.ProviderIds.Count() > 0)
                                {
                                    KeyValuePair<string, string> firstProvider = artist.ProviderIds.FirstOrDefault();

                                    string providerUrl = firstProvider.Key == "MusicBrainzArtist" ? $"https://musicbrainz.org/artist/{firstProvider.Value}" : $"https://theaudiodb.com/artist/{firstProvider.Value}";

                                    return $"[{artist.Name}]({providerUrl})";
                                }
                                else
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

                        // populate title URL
                        if (!String.IsNullOrEmpty(sysInfo.WanAddress) && !options.ExcludeExternalServerLinks) mediaAddedEmbed.embeds.First().url = $"{sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={sysInfo.Id}";

                        // populate images
                        if (item.HasImage(ImageType.Primary))
                        {
                            string imageUrl = "";

                            if (!item.GetImageInfo(ImageType.Primary, 0).IsLocalFile)
                            {
                                imageUrl = item.GetImagePath(ImageType.Primary);
                            }
                            else if (serverConfig.EnableRemoteAccess == true && !options.ExcludeExternalServerLinks) // in the future we can proxy images through memester server if people want to hide their server address
                            {
                                imageUrl = $"{sysInfo.WanAddress}/emby/Items/{itemId}/Images/Primary";
                            }
                            else
                            {
                                string localPath = item.GetImagePath(ImageType.Primary);
                                Stream imageData = _fileSystem.OpenRead(localPath);

                                try
                                {
                                    imageUrl = await MemesterServiceHelper.UploadImage(imageData, _jsonSerializer, _httpClient);
                                }
                                catch (Exception e)
                                {
                                    _logger.ErrorException("Failed to proxy image", e);
                                }
                            }

                            mediaAddedEmbed.embeds.First().thumbnail = new Thumbnail
                            {
                                url = imageUrl
                            };
                        }


                        if (options.MentionType == MentionTypes.Everyone)
                        {
                            mediaAddedEmbed.content = "@everyone";
                        }
                        else if (options.MentionType == MentionTypes.Here)
                        {
                            mediaAddedEmbed.content = "@here";
                        }

                        // populate external URLs
                        List<Field> providerFields = new List<Field>();

                        if (!localMetadataFallback)
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
                    }
                    else
                    {
                        queuedUpdateCheck[itemId]++;

                        _logger.Debug("Attempt: {0}", queuedUpdateCheck[itemId]);
                    }
                });
            }
            catch (Exception e)
            {
                _logger.ErrorException("Something unexpected happened in the item update checker", e);
            }
        }

        // in js i'd just do a slice(-2) but i couldnt find a cs equiv

        private class MemesterServiceResponse
        {
            public string filePath { get; set; }
        }

        private string formatIndex(int? number) => number < 10 ? $"0{number}" : number.ToString();

        private void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            BaseItem Item = changeEvent.Item;
            DiscordOptions options = Plugin.Instance.Configuration.Options.FirstOrDefault(opt => opt.MediaAddedOverride == true);

            string LibraryType = Item.GetType().Name;

            if (!Item.IsVirtualItem && Array.Exists(Constants.AllowedMediaTypes, t => t == LibraryType) && options != null)
            {
                queuedUpdateCheck.Add(Item.Id, 0);
            }
        }

        private bool isInVisibleLibrary(string UserId, BaseItem item)
        {
            Boolean isIn = false;

            _userViewManager.GetUserViews(
                new UserViewQuery
                {
                    UserId = _userManager.GetInternalId(Guid.Parse(UserId))
                }
            ).ToList().ForEach(folder => {
                if (folder.GetItemIdList(new InternalItemsQuery { }).Contains(item.InternalId))
                {
                    isIn = true;
                }
            });

            return isIn;
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

                    _logger.Debug("Pending queue count second: {0}", pendingSendQueue.Count());

                }
            });
        }
    }
}