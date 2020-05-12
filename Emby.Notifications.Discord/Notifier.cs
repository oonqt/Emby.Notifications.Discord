using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Emby.Notifications.Discord.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using System.Timers;

// FUTURE: once we get the quirks worked out, we will remove the need for emby's built in notification system because it sucks and implement little modules for each notification (media added, plugin update, etc)

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly ILibraryManager _libraryManager;
        private readonly IServerApplicationHost _applicationHost;
        private readonly ILocalizationManager _localizationManager;
        private readonly IUserViewManager _userViewManager;
        private readonly IUserManager _userManager;

        private System.Timers.Timer QueuedMessageHandler;
        private System.Timers.Timer QueuedUpdateHandler;
        private Dictionary<Guid, QueuedUpdateData> queuedUpdateCheck = new Dictionary<Guid, QueuedUpdateData> { };
        private Dictionary<DiscordMessage, DiscordOptions> pendingSendQueue = new Dictionary<DiscordMessage, DiscordOptions> { };

        public Notifier(ILogManager logManager, IJsonSerializer jsonSerializer, IServerConfigurationManager serverConfiguration, ILibraryManager libraryManager, IServerApplicationHost applicationHost, ILocalizationManager localizationManager, IUserViewManager userViewManager, IUserManager userManager)
        {
            _logger = logManager.GetLogger(GetType().Namespace);
            _jsonSerializer = jsonSerializer;
            _serverConfiguration = serverConfiguration;
            _libraryManager = libraryManager;
            _localizationManager = localizationManager;
            _applicationHost = applicationHost;
            _userViewManager = userViewManager;
            _userManager = userManager;

            QueuedMessageHandler = new System.Timers.Timer(Constants.MessageQueueSendInterval);
            QueuedMessageHandler.AutoReset = true;
            QueuedMessageHandler.Elapsed += QueuedMessageSender;
            QueuedMessageHandler.Start();

            QueuedUpdateHandler = new System.Timers.Timer(Constants.RecheckIntervalMS);
            QueuedUpdateHandler.AutoReset = true;
            QueuedUpdateHandler.Elapsed += CheckForMetadata;
            QueuedUpdateHandler.Start();

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");
        }

        public void Dispose() {
            _libraryManager.ItemAdded -= ItemAddHandler;
            QueuedMessageHandler.Stop();
            QueuedMessageHandler.Dispose();
            QueuedUpdateHandler.Stop();
            QueuedUpdateHandler.Dispose();
        }

        private async void QueuedMessageSender(object sender, ElapsedEventArgs eventArgs)
        {
            try
            {
                if (pendingSendQueue.Count > 0)
                {
                    DiscordMessage message = pendingSendQueue.First().Key;
                    DiscordOptions options = pendingSendQueue.First().Value;

                    await DiscordWebhookHelper.ExecuteWebhook(message, options.DiscordWebhookURI, _jsonSerializer);

                    if(pendingSendQueue.ContainsKey(message)) pendingSendQueue.Remove(message);
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
                int queueCount = queuedUpdateCheck.Count();
                if(queueCount > 0) _logger.Debug("Item in queue : {0}", queueCount);

                queuedUpdateCheck.ToList().ForEach(async queuedItem =>
                {
                    DiscordOptions options = queuedItem.Value.Configuration;
                    Guid itemId = queuedItem.Value.ItemId;

                    _logger.Debug("{0} queued for recheck", itemId.ToString());

                    BaseItem item = _libraryManager.GetItemById(itemId);

                    LibraryOptions itemLibraryOptions = _libraryManager.GetLibraryOptions(item);
                    PublicSystemInfo sysInfo = await _applicationHost.GetPublicSystemInfo(CancellationToken.None);
                    ServerConfiguration serverConfig = _serverConfiguration.Configuration;

                    string LibraryType = item.GetType().Name;
                    string serverName = options.ServerNameOverride ? serverConfig.ServerName : "Emby Server";

                    // for whatever reason if you have extraction on during library scans then it waits for the extraction to finish before populating the metadata.... I don't get why the fuck it goes in that order
                    // its basically impossible to make a prediction on how long it could take as its dependent on the bitrate, duration, codec, and processing power of the system
                    Boolean localMetadataFallback = queuedUpdateCheck[queuedItem.Key].Retries >= (itemLibraryOptions.ExtractChapterImagesDuringLibraryScan ? Constants.MaxRetriesBeforeFallback * 5.5 : Constants.MaxRetriesBeforeFallback);

                    if (item.ProviderIds.Count > 0 || localMetadataFallback)
                    {
                        _logger.Debug("{0}[{1}] has metadata (Local fallback: {2}), sending notification to {3}", item.Id, item.Name, localMetadataFallback, options.MediaBrowserUserId);

                        if(queuedUpdateCheck.ContainsKey(queuedItem.Key)) queuedUpdateCheck.Remove(queuedItem.Key); // remove it beforehand because if some operation takes any longer amount of time it might allow enough time for another notification to slip through

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
                            titleText = $"{item.Parent.Parent.Name}{(item.ParentIndexNumber.HasValue ? $" S{formatIndex(item.ParentIndexNumber)}" : "")}{(item.IndexNumber.HasValue ? $"E{formatIndex(item.IndexNumber)}" : "")} {item.Name}";
                        } else if (LibraryType == "Season") {
                            titleText = $"{item.Parent.Name} {item.Name}";
                        }
                        else
                        {
                            titleText = $"{item.Name}{(item.ProductionYear.HasValue ? $" ({item.ProductionYear.ToString()})" : "")}";
                        }

                        mediaAddedEmbed.embeds.First().title = $"{titleText} has been added to {serverName.Trim()}";

                        // populate description
                        if (LibraryType == "Audio")
                        {
                            List<BaseItem> artists = _libraryManager.GetAllArtists(item);

                            IEnumerable<string> artistsFormat = artists.Select(artist =>
                            {
                                string formattedArtist = artist.Name;

                                if (artist.ProviderIds.Count() > 0)
                                {
                                    KeyValuePair<string, string> firstProvider = artist.ProviderIds.FirstOrDefault();

                                    string providerUrl = firstProvider.Key == "MusicBrainzArtist" ? $"https://musicbrainz.org/artist/{firstProvider.Value}" : $"https://theaudiodb.com/artist/{firstProvider.Value}";

                                    formattedArtist += $" [(Music Brainz)]({providerUrl})";
                                }

                                if(serverConfig.EnableRemoteAccess && !options.ExcludeExternalServerLinks) formattedArtist += $" [(Emby)]({sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={artist.InternalId})";

                                return formattedArtist;
                            });

                            if (artists.Count() > 0) mediaAddedEmbed.embeds.First().description = $"By {string.Join(", ", artistsFormat)}";
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(item.Overview)) mediaAddedEmbed.embeds.First().description = item.Overview;
                        }

                        // populate title URL
                        if (serverConfig.EnableRemoteAccess && !options.ExcludeExternalServerLinks) mediaAddedEmbed.embeds.First().url = $"{sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={sysInfo.Id}";

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

                                try
                                {
                                    ImageServiceResponse response = MemesterServiceHelper.UploadImage(localPath, _jsonSerializer);
                                    imageUrl = response.filePath;
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
                                    name = "External Links"
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
                    }
                    else
                    {
                        if(queuedUpdateCheck.ContainsKey(queuedItem.Key)) queuedUpdateCheck[queuedItem.Key].Retries++;
                    }
                });
            }
            catch (Exception e)
            {
                _logger.ErrorException("Something unexpected happened in the item update checker", e);
            }
        }

        // in js i'd just do a slice(-2) but i couldnt find a cs equiv
        private string formatIndex(int? number) => number < 10 ? $"0{number}" : number.ToString();

        private void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            BaseItem Item = changeEvent.Item;
            string LibraryType = Item.GetType().Name;

            Plugin.Instance.Configuration.Options.ToList().ForEach(options => {
                List<string> allowedItemTypes = new List<string> {};
                if(options.EnableAlbums) allowedItemTypes.Add("MusicAlbum");
                if(options.EnableMovies) allowedItemTypes.Add("Movie");
                if(options.EnableEpisodes) allowedItemTypes.Add("Episode");
                if(options.EnableSeries) allowedItemTypes.Add("Series");
                if(options.EnableSeasons) allowedItemTypes.Add("Season");
                if(options.EnableSongs) allowedItemTypes.Add("Audio");

                if (
                    !Item.IsVirtualItem 
                    && Array.Exists(allowedItemTypes.ToArray(), t => t == LibraryType) 
                    && options != null 
                    && options.Enabled == true 
                    && options.MediaAddedOverride == true
                    && isInVisibleLibrary(options.MediaBrowserUserId, Item)
                )
                {
                    queuedUpdateCheck.Add(Guid.NewGuid(), new QueuedUpdateData { Retries = 0, Configuration = options, ItemId = Item.Id });
                }
            });
        }

        private class QueuedUpdateData {
            public int Retries { get; set; }
            public Guid ItemId { get; set; }
            public DiscordOptions Configuration { get; set; }
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
                if (folder.GetItemIdList(new InternalItemsQuery { IncludeItemTypes = new string[] { "MusicAlbum", "Movie", "Episode", "Series", "Season", "Audio" }, Recursive = true }).Contains(item.InternalId))
                {
                    isIn = true;
                }
            });

            return isIn;
        }

        public bool IsEnabledForUser(User user)
        {
            DiscordOptions options = GetOptions(user);

            return options != null && options.Enabled;
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

                // Once we remove the need for the built in notification system, this hacky method can be replaced with something better
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