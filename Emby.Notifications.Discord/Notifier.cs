using System.Collections.Generic;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;
using Emby.Notifications.Discord.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Notifications.Discord
{
    public class Notifier : INotificationService
    {
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        public Notifier(ILogManager logManager, IHttpClient httpClient)
        {
            _logger = logManager.GetLogger(GetType().Name);
            _httpClient = httpClient;
        }

        public bool IsEnabledForUser(User user)
        {
            var options = GetOptions(user);

            return options != null && IsValid(options) && options.Enabled;
        }

        private DiscordOptions GetOptions(User user)
        {
            return Plugin.Instance.Configuration.Options
                .FirstOrDefault(i => string.Equals(i.MediaBrowserUserId, user.Id.ToString("N"), StringComparison.OrdinalIgnoreCase));
        }

        public string Name
        {
            get { return Plugin.Instance.Name; }
        }

        public async Task SendNotification(UserNotification request, CancellationToken cancellationToken)
        {
            var options = GetOptions(request.User);

            string message;

            if(string.IsNullOrEmpty(request.Description))
            {
                message = request.Name;
            
            }
            else
            {
                message = request.Name + "\r\n" + request.Description;
            }

            var parameters = new Dictionary<string, string> { };
            parameters.Add("username", options.Username);
            parameters.Add("avatar_url", options.AvatarUrl);
            parameters.Add("content", message);

            _logger.Debug("Discord Request to: {0} From: {1}", options.DiscordWebhookURI, request.User);

            var httpRequestOptions = new HttpRequestOptions { };

            httpRequestOptions.Url = options.DiscordWebhookURI;
            httpRequestOptions.RequestHeaders["Content-Type"] = "application/json";
            httpRequestOptions.SetPostData(parameters);

            using (await _httpClient.Post(httpRequestOptions).ConfigureAwait(false))
            {

            }
            
        }

        private bool IsValid(DiscordOptions options)
        {
            return !string.IsNullOrEmpty(options.DiscordWebhookURI);
        }
    }
}
