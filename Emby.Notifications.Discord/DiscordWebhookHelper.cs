using System;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Emby.Notifications.Discord
{
    public class DiscordEmbed
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

    public class Thumbnail
    {
        public string url { get; set; }
    }

    public class Field
    {
        public string name { get; set; }
        public string value { get; set; }
        public Boolean inline { get; set; }
    }

    public class Footer
    {
        public string text { get; set; }
        public string icon_url { get; set; }
    }

    public class DiscordMessage
    {
        public string avatar_url { get; set; }
        public string username { get; set; }
        public string content { get; set; }
        public List<DiscordEmbed> embeds { get; set; }
    }

    public class DiscordWebhookHelper
    {
        public static int FormatColorCode(string hexCode)
        {
            return int.Parse(hexCode.Substring(1, 6), System.Globalization.NumberStyles.HexNumber);
        }

        public static async Task ExecuteWebhook(DiscordMessage message, string webhookUrl, IJsonSerializer _jsonSerializer, HttpClient _httpClient)
        {
            StringContent postData = new StringContent(_jsonSerializer.SerializeToString(message).ToString());

            try
            {
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
                req.Content = postData;
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                HttpResponseMessage response = await _httpClient.SendAsync(req).ConfigureAwait(false);

                string content = await response.Content.ReadAsStringAsync();

                if(response.StatusCode != HttpStatusCode.NoContent) {
                    throw new System.Exception($"Status: {response.StatusCode} content: {content}");
                }
            }
            catch (HttpRequestException e)
            {
                throw new System.Exception(e.Message);
            }
        }
    }
}
