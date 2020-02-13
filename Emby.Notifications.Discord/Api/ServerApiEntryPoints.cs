using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using Emby.Notifications.Discord.Configuration;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;
using System.Net.Http;
using System.Net.Http.Headers;

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
        private readonly IJsonSerializer _jsonSerializer;

        public ServerApiEndpoints(ILogManager logManager, IJsonSerializer jsonSerializer)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _httpClient = new HttpClient();
            _jsonSerializer = jsonSerializer;
        }
        private DiscordOptions GetOptions(String userID)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, userID, StringComparison.OrdinalIgnoreCase));
        }

        public void Post(TestNotification request)
        {
            var task = PostAsync(request);
            Task.WaitAll(task);
        }

        public class DiscordEmbed
        {
            public int color { get; set; }
            public string title { get; set; }
            public string description { get; set; }
        }

        public class DiscordMessage
        {
            public List<DiscordEmbed> embeds { get; set; }
            public string username { get; set; }
            public string avatar_url { get; set; }
        }

        public async Task PostAsync(TestNotification request)
        {
            var options = GetOptions(request.UserID);

            var postData = new StringContent(_jsonSerializer.SerializeToString(new DiscordMessage()
            {
                avatar_url = options.AvatarUrl,
                username = options.Username,
                embeds = new List<DiscordEmbed>()
                {
                    new DiscordEmbed()
                    {
                        color = 181818,
                        description = "That's RIGHT",
                        title = "HA"
                    }
                }
            }).ToString());

            _logger.Debug("Discord Request to: {0} From: {1}", options.DiscordWebhookURI, request.UserID);

            try
            {
                var RequestMessage = new HttpRequestMessage(HttpMethod.Post, options.DiscordWebhookURI);
                RequestMessage.Content = postData;
                RequestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                await _httpClient.SendAsync(RequestMessage).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                _logger.Error("Failed to make request to Discord: {0}", e);
            }
        }
    }
}
