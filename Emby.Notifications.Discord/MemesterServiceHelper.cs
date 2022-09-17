using System;
using System.Net;
using System.Text;
using MediaBrowser.Model.Serialization;

namespace Emby.Notifications.Discord
{
    public class ImageServiceResponse
    {
        public string filePath { get; set; }
    }

    public class MemesterServiceHelper
    {
        private static string uploadEndpoint => "https://i.memester.xyz/upload?format=json";

        public static ImageServiceResponse UploadImage(string path, IJsonSerializer jsonSerializer)
        {
            try
            {
                var client = new WebClient();

                var response = client.UploadFile(uploadEndpoint, path);

                return jsonSerializer.DeserializeFromString<ImageServiceResponse>(Encoding.Default.GetString(response));
            }
            catch (Exception e)
            {
                throw e;
            }
        }
    }
}