using System.Runtime.CompilerServices;
using System.Net.Http.Headers;
using System.Net;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System.Net.Http;
using System.IO;
using System;

namespace Emby.Notifications.Discord
{
    public class ImageServiceResponse
    {
        public string filePath { get; set; }
    }

    public class MemesterServiceHelper {
        private static string uploadEndpoint => "https://i.memester.xyz/upload?format=json";

        public static async Task<string> UploadImage(Stream ImageData, IJsonSerializer jsonSerializer, HttpClient httpClient) {
            StreamContent imageStream = new StreamContent(ImageData);
            ByteArrayContent imageStreamContent = new ByteArrayContent(await imageStream.ReadAsByteArrayAsync());
            MultipartFormDataContent formData = new MultipartFormDataContent();

            imageStreamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data");

            formData.Add(imageStreamContent, "file", "poster.png");

            try {
                HttpResponseMessage res = await httpClient.PostAsync(uploadEndpoint, formData);

                string responseContent = await res.Content.ReadAsStringAsync();

                ImageServiceResponse memesterResponse = jsonSerializer.DeserializeFromString<ImageServiceResponse>(responseContent);

                if(res.StatusCode == HttpStatusCode.Created) {
                    return memesterResponse.filePath;
                } else {
                    throw new Exception($"Status: {res.StatusCode} Server Response: {responseContent}");
                }
            } catch (HttpRequestException e) {
                throw new Exception(e.Message);
            }
        }
    }
}