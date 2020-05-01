using System.Net;
using System.Threading.Tasks;
using MediaBrowser.Model.Serialization;
using System;
using System.Text;

namespace Emby.Notifications.Discord
{
    public class ImageServiceResponse
    {
        public string filePath { get; set; }
    }

    public class MemesterServiceHelper {
        private static string uploadEndpoint => "https://i.memester.xyz/upload?format=json";

        public static ImageServiceResponse UploadImage(string path, IJsonSerializer jsonSerializer) {
            try
            {
                WebClient client = new WebClient();

                byte[] response = client.UploadFile(uploadEndpoint, path);

                return jsonSerializer.DeserializeFromString<ImageServiceResponse>(Encoding.Default.GetString(response));
            } catch (Exception e)
            {
                throw e;
            }
        }
    }
}