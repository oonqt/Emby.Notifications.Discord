﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;

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
        public bool inline { get; set; }
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
            return int.Parse(hexCode.Substring(1, 6), NumberStyles.HexNumber);
        }

        public static async Task ExecuteWebhook(DiscordMessage message, string webhookUrl, IJsonSerializer _jsonSerializer)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(_jsonSerializer.SerializeToString(message));

                var request = (HttpWebRequest) WebRequest.Create(webhookUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                using (var requestData = request.GetRequestStream())
                {
                    requestData.Write(bytes, 0, bytes.Count());
                }

                var response = (HttpWebResponse) await request.GetResponseAsync();

                response.Dispose();
            }
            catch (Exception e)
            {
                throw e;
            }
        }
    }
}