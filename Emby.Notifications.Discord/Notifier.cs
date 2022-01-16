using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Emby.Notifications.Discord.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using Timer = System.Timers.Timer;

// FUTURE: once we get the quirks worked out, we will remove the need for emby's built in notification system because it sucks and implement little modules for each notification (media added, plugin update, etc)

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService, IDisposable
    {
        private readonly IServerApplicationHost _applicationHost;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILibraryManager _libraryManager;
        private readonly ILocalizationManager _localizationManager;
        private readonly ILogger _logger;
        private readonly IServerConfigurationManager _serverConfiguration;
        private readonly IUserManager _userManager;
        private readonly IUserViewManager _userViewManager;
        private readonly Dictionary<DiscordMessage, DiscordOptions> pendingSendQueue = new Dictionary<DiscordMessage, DiscordOptions>();

        private readonly Timer QueuedMessageHandler;
        private readonly Dictionary<Guid, QueuedUpdateData> queuedUpdateCheck = new Dictionary<Guid, QueuedUpdateData>();
        private readonly Timer QueuedUpdateHandler;

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

            QueuedMessageHandler = new Timer(Constants.MessageQueueSendInterval);
            QueuedMessageHandler.AutoReset = true;
            QueuedMessageHandler.Elapsed += QueuedMessageSender;
            QueuedMessageHandler.Start();

            QueuedUpdateHandler = new Timer(Constants.RecheckIntervalMS);
            QueuedUpdateHandler.AutoReset = true;
            QueuedUpdateHandler.Elapsed += CheckForMetadata;
            QueuedUpdateHandler.Start();

            _libraryManager.ItemAdded += ItemAddHandler;
            _logger.Debug("Registered ItemAdd handler");
        }

        public void Dispose()
        {
            _libraryManager.ItemAdded -= ItemAddHandler;
            QueuedMessageHandler.Stop();
            QueuedMessageHandler.Dispose();
            QueuedUpdateHandler.Stop();
            QueuedUpdateHandler.Dispose();
        }

        public bool IsEnabledForUser(User user)
        {
            var options = GetOptions(user);

            return options != null && options.Enabled;
        }

        public string Name => Plugin.Instance.Name;

        public async Task SendNotification(UserNotification request, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    var options = GetOptions(request.User);

                    // Once we remove the need for the built in notification system, this hacky method can be replaced with something better
                    if (options.MediaAddedOverride && !request.Name.Contains(_localizationManager.GetLocalizedString("ValueHasBeenAddedToLibrary").Replace("{0} ", "").Replace(" {1}", "")) || !options.MediaAddedOverride)
                    {
                        var serverName = _serverConfiguration.Configuration.ServerName;

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

                        var discordMessage = new DiscordMessage
                        {
                            avatar_url = options.AvatarUrl,
                            username = options.Username,
                            embeds = new List<DiscordEmbed>
                            {
                                new DiscordEmbed
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
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Something unexpected happened in WebHook", ex);
                }
            });
        }

        private async void QueuedMessageSender(object sender, ElapsedEventArgs eventArgs)
        {
            try
            {
                if (pendingSendQueue.Count > 0)
                {
                    var message = pendingSendQueue.First().Key;
                    var options = pendingSendQueue.First().Value;

                    await DiscordWebhookHelper.ExecuteWebhook(message, options.DiscordWebhookURI, _jsonSerializer);

                    if (pendingSendQueue.ContainsKey(message)) pendingSendQueue.Remove(message);
                }
            }
            catch (Exception e)
            {
                _logger.ErrorException("Failed to execute webhook: ", e);
            }
        }

        private void CheckForMetadata(object sender, ElapsedEventArgs eventArgs)
        {
            try
            {
                var queueCount = queuedUpdateCheck.Count();
                if (queueCount > 0) _logger.Debug("Item in queue : {0}", queueCount);

                queuedUpdateCheck.ToList().ForEach(async queuedItem =>
                {
                    try
                    {
                        // sometimes an update check might execute while another one is hanging and causes crash !
                        if (queuedUpdateCheck.ContainsKey(queuedItem.Key))
                        {
                            var options = queuedItem.Value.Configuration;
                            var itemId = queuedItem.Value.ItemId;

                            _logger.Debug("{0} queued for recheck", itemId.ToString());

                            var item = _libraryManager.GetItemById(itemId);

                            var itemLibraryOptions = _libraryManager.GetLibraryOptions(item);
                            var sysInfo = await _applicationHost.GetPublicSystemInfo(CancellationToken.None);
                            var serverConfig = _serverConfiguration.Configuration;

                            var LibraryType = item.GetType().Name;
                            var serverName = options.ServerNameOverride ? serverConfig.ServerName : "Emby Server";

                            if (string.IsNullOrEmpty(serverName))
                                serverName = "Emby Server";

                            // for whatever reason if you have extraction on during library scans then it waits for the extraction to finish before populating the metadata.... I don't get why the fuck it goes in that order
                            // its basically impossible to make a prediction on how long it could take as its dependent on the bitrate, duration, codec, and processing power of the system
                            var localMetadataFallback = queuedUpdateCheck[queuedItem.Key].Retries >= (itemLibraryOptions.ExtractChapterImagesDuringLibraryScan ? Constants.MaxRetriesBeforeFallback * 5.5 : Constants.MaxRetriesBeforeFallback);

                            if (item.ProviderIds.Count > 0 || localMetadataFallback)
                            {
                                _logger.Debug("{0}[{1}] has metadata (Local fallback: {2}), adding to queue", item.Id, item.Name, localMetadataFallback, options.MediaBrowserUserId);

                                if (queuedUpdateCheck.ContainsKey(queuedItem.Key)) queuedUpdateCheck.Remove(queuedItem.Key); // remove it beforehand because if some operation takes any longer amount of time it might allow enough time for another notification to slip through

                                // build primary info 
                                var mediaAddedEmbed = new DiscordMessage
                                {
                                    username = options.Username,
                                    avatar_url = options.AvatarUrl,
                                    embeds = new List<DiscordEmbed>
                                    {
                                        new DiscordEmbed
                                        {
                                            color = DiscordWebhookHelper.FormatColorCode(options.EmbedColor),
                                            footer = new Footer
                                            {
                                                text = $"From {serverName}",
                                                icon_url = options.AvatarUrl
                                            },
                                            timestamp = DateTime.Now
                                        }
                                    }
                                };

                                // populate title
                                string titleText;
                                if (LibraryType == "Episode")
                                    titleText = $"{item.Parent.Parent.Name}{(item.ParentIndexNumber.HasValue ? $" S{formatIndex(item.ParentIndexNumber)}" : "")}{(item.IndexNumber.HasValue ? $"E{formatIndex(item.IndexNumber)}" : "")} {item.Name}";
                                else if (LibraryType == "Season")
                                    titleText = $"{item.Parent.Name} {item.Name}";
                                else
                                    titleText = $"{item.Name}{(item.ProductionYear.HasValue ? $" ({item.ProductionYear.ToString()})" : "")}";

                                mediaAddedEmbed.embeds.First().title = $"{titleText} has been added to {serverName.Trim()}";

                                // populate description
                                if (LibraryType == "Audio")
                                {
                                    var artists = _libraryManager.GetAllArtists(item);

                                    var artistsFormat = artists.Select(artist =>
                                    {
                                        var formattedArtist = artist.Name;

                                        if (artist.ProviderIds.Count() > 0)
                                        {
                                            var firstProvider = artist.ProviderIds.FirstOrDefault();

                                            var providerUrl = firstProvider.Key == "MusicBrainzArtist" ? $"https://musicbrainz.org/artist/{firstProvider.Value}" : $"https://theaudiodb.com/artist/{firstProvider.Value}";

                                            formattedArtist += $" [(Music Brainz)]({providerUrl})";
                                        }

                                        if (serverConfig.EnableRemoteAccess && !options.ExcludeExternalServerLinks) formattedArtist += $" [(Emby)]({sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={artist.InternalId})";

                                        return formattedArtist;
                                    });

                                    if (artists.Count() > 0) mediaAddedEmbed.embeds.First().description = $"By {string.Join(", ", artistsFormat)}";
                                }
                                else
                                {
                                    if (!string.IsNullOrEmpty(item.Overview)) mediaAddedEmbed.embeds.First().description = item.Overview;
                                }

                                // populate title URL
                                if (serverConfig.EnableRemoteAccess && !options.ExcludeExternalServerLinks) mediaAddedEmbed.embeds.First().url = $"{sysInfo.WanAddress}/web/index.html#!/item?id={itemId}&serverId={sysInfo.Id}";

                                // populate images
                                if (item.HasImage(ImageType.Primary))
                                {
                                    var imageUrl = "";

                                    if (!item.GetImageInfo(ImageType.Primary, 0).IsLocalFile)
                                    {
                                        imageUrl = item.GetImagePath(ImageType.Primary);
                                    }
                                    else if (serverConfig.EnableRemoteAccess && !options.ExcludeExternalServerLinks) // in the future we can proxy images through memester server if people want to hide their server address
                                    {
                                        imageUrl = $"{sysInfo.WanAddress}/emby/Items/{itemId}/Images/Primary";
                                    }
                                    else
                                    {
                                        var localPath = item.GetImagePath(ImageType.Primary);

                                        try
                                        {
                                            var response = MemesterServiceHelper.UploadImage(localPath, _jsonSerializer);
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
                                    mediaAddedEmbed.content = "@everyone";
                                else if (options.MentionType == MentionTypes.Here)
                                    mediaAddedEmbed.content = "@here";

                                // populate external URLs
                                var providerFields = new List<Field>();

                                if (!localMetadataFallback)
                                {
                                    item.ProviderIds.ToList().ForEach(provider =>
                                    {
                                        var field = new Field
                                        {
                                            name = "External Links"
                                        };

                                        var didPopulate = true;
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

                                        if (didPopulate) providerFields.Add(field);
                                    });

                                    if (providerFields.Count() > 0) mediaAddedEmbed.embeds.First().fields = providerFields;
                                }

                                pendingSendQueue.Add(mediaAddedEmbed, options);
                            }
                            else
                            {
                                if (queuedUpdateCheck.ContainsKey(queuedItem.Key)) queuedUpdateCheck[queuedItem.Key].Retries++;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.ErrorException("Something unexpected happened in the item update checker", e);
                    }
                });
            }
            catch (Exception e)
            {
                _logger.ErrorException("Something unexpected happened in the item update checker", e);
            }
        }

        // in js i'd just do a slice(-2) but i couldnt find a cs equiv
        private string formatIndex(int? number)
        {
            return number < 10 ? $"0{number}" : number.ToString();
        }

        private void ItemAddHandler(object sender, ItemChangeEventArgs changeEvent)
        {
            try
            {
                var Item = changeEvent.Item;
                var LibraryType = Item.GetType().Name;

                Plugin.Instance.Configuration.Options.ToList().ForEach(options =>
                {
                    try
                    {
                        var allowedItemTypes = new List<string>();
                        if (options.EnableAlbums) allowedItemTypes.Add("MusicAlbum");
                        if (options.EnableMovies) allowedItemTypes.Add("Movie");
                        if (options.EnableEpisodes) allowedItemTypes.Add("Episode");
                        if (options.EnableSeries) allowedItemTypes.Add("Series");
                        if (options.EnableSeasons) allowedItemTypes.Add("Season");
                        if (options.EnableSongs) allowedItemTypes.Add("Audio");

                        if (
                            !Item.IsVirtualItem
                            && Array.Exists(allowedItemTypes.ToArray(), t => t == LibraryType)
                            && options != null
                            && options.Enabled
                            && options.MediaAddedOverride
                            && isInVisibleLibrary(options.MediaBrowserUserId, Item)
                        )
                            queuedUpdateCheck.Add(Guid.NewGuid(), new QueuedUpdateData {Retries = 0, Configuration = options, ItemId = Item.Id});
                    }
                    catch (Exception e)
                    {
                        _logger.ErrorException("Something unexpected happened in the ItemAddHandler", e);
                    }
                });
            }
            catch (Exception e)
            {
                _logger.ErrorException("Something unexpected happened in the ItemAddHandler", e);
            }
        }

        private bool isInVisibleLibrary(string UserId, BaseItem item)
        {
            var isIn = false;

            try
            {
                _userViewManager.GetUserViews(
                    new UserViewQuery
                    {
                        UserId = _userManager.GetInternalId(Guid.Parse(UserId))
                    }
                ).ToList().ForEach(folder =>
                {
                    try
                    {
                        if (folder.GetItemIdList(new InternalItemsQuery {IncludeItemTypes = new[] {"MusicAlbum", "Movie", "Episode", "Series", "Season", "Audio"}, Recursive = true}).Contains(item.InternalId))
                            isIn = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Something unexpected happened in isInVisibleLibrary (Potentially options.MediaBrowserUserId is null). UserID = " + UserId, ex);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Something unexpected happened in isInVisibleLibrary (Potentially options.MediaBrowserUserId is null). UserID = " + UserId, ex);
            }

            return isIn;
        }

        private DiscordOptions GetOptions(User user)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, user.Id.ToString("N"), StringComparison.OrdinalIgnoreCase));
        }

        private class QueuedUpdateData
        {
            public int Retries { get; set; }
            public Guid ItemId { get; set; }
            public DiscordOptions Configuration { get; set; }
        }
    }
}