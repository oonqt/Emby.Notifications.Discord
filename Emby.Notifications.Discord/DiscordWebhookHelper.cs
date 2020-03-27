using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Emby.Notifications.Discord
{
    class DiscordEmbed
    {
        public int color { get; set; }
        public string title { get; set; }
        public string url { get; set; }
        public string description { get; set; }
        public Thumbnail thumbnail { get; set; }
        public List<Field> fields { get; set; }
        public Footer footer { get; set; }
        public DateTime timestamp { get; set; }
    }

    class Thumbnail
    {
        public string url { get; set; }
    }

    class Field
    {
        public string name { get; set; }
        public string value { get; set; }
        public Boolean inline { get; set; }
    }

    class Footer
    {
        public string text { get; set; }
        public string icon_url { get; set; }
    }

    class DiscordMessage
    {
        public string avatar_url { get; set; }
        public string username { get; set; }
        public string content { get; set; }
        public List<DiscordEmbed> embeds { get; set; }
    }

    class DiscordWebhookHelper
    {
        public static int FormatColorCode(string hexCode)
        {
            return int.Parse(hexCode.Substring(1, 6), System.Globalization.NumberStyles.HexNumber);
        }

        public static async Task ExecuteWebhook(DiscordMessage message, string webhookUrl, IJsonSerializer _jsonSerializer, ILogger _logger, HttpClient _httpClient)
        {
            StringContent postData = new StringContent(_jsonSerializer.SerializeToString(message).ToString());

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
                req.Content = postData;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage response = await _httpClient.SendAsync(req).ConfigureAwait(false);


                _logger.Debug("Request to {0} completed with status {1}", req.RequestUri, response.StatusCode);
            }
            catch (HttpRequestException e)
            {
                _logger.Error("Failed to make request to Discord: {0}", e);
                throw new ArgumentException(); // return 400 response
            }
        }
    }
}
