using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using Emby.Notifications.Discord.Configuration;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;

        public ServerApiEndpoints(ILogManager logManager, IHttpClient httpClient)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _httpClient = httpClient;
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

        public async Task PostAsync(TestNotification request)
        {
            var options = GetOptions(request.UserID);

            var parameters = new Dictionary<string, string> { };
            parameters.Add("content", "This is a test notification from Emby");
            parameters.Add("username", options.Username);
            parameters.Add("avatar_url", options.AvatarUrl);
             
            _logger.Debug("Discord Request to: {0} From: {1}", options.DiscordWebhookURI, request.UserID);

            var httpRequestOptions = new HttpRequestOptions { };

            httpRequestOptions.Url = options.DiscordWebhookURI;
            httpRequestOptions.RequestHeaders["Content-Type"] = "application/json";
            httpRequestOptions.SetPostData(parameters);

            using (await _httpClient.Post(httpRequestOptions).ConfigureAwait(false))
            {

            }
        }
    }
}
