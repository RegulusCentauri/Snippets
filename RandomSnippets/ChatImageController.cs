//Aug 2022

using System;
using System.Linq;
using System.Web.Http;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Net.Mime;

using HexCorp.ReactClient.Code;
using HexCorp.General.Common;
using HexCorp.Model.Client;

using Newtonsoft.Json;

#if V4CORE
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using HexCorp.General.Model.Lqml;
using RO = HexCorp.General.Model.Lqml;
#else
using HexCorp.Model.Dbml;
#endif

namespace HexCorp.ReactClient.Controllers
{
    [RoutePrefix("api/chatimage")]
    [Authorize]
    public class ChatImageController : Controller
    {
        static ExpiringCache<ImageDataPoco> SocialMediaImageCache = new ExpiringCache<ImageDataPoco>(1800, "ChatImageController.ChatImage"); //30 min cache
        string tyntecdMediaUrl = "https://api.tyntec.com/conversations/v3/media/"; //Leaving this as a default
        string mediaUrl = "";
        
        [HttpGet, Route("{chatId:guid}/{mediaId:maxlength(100)}")]
        [ClientExceptionFilter]
#if V4CORE
        public IActionResult ChatImage([FromUri] Guid chatId, [FromUri] string mediaId) {
#else
        public IHttpActionResult ChatImage([FromUri] Guid chatId, [FromUri] string mediaId) {
#endif
            Dbg.WriteLine($"Starting ChatImage for ChatId {chatId} and mediaId {mediaId}", "ChatImageController.ChatImage");
            try {
                var key = chatId.ToString() + "_" + mediaId;
                var imageDataRecordCached = SocialMediaImageCache.TryGet(key)?.Item;
                if(imageDataRecordCached != null) {
                    Dbg.WriteLine($"ChatImg found in cache", "ChatImageController.ChatImage");
                    return SendToBrowser(imageDataRecordCached);
                }
                else {
                    Dbg.WriteLine($"ChatImg not found in cache", "ChatImageController.ChatImage");
                    using(var ctx = new DbDataContext()) {
                        var chatGatewayId = ctx.Chats.FirstOrDefault(y => y.ChatId == chatId).ChatGatewayId;
                        var chatGatewayDevice = ctx.ChatGateways.FirstOrDefault(x => x.ChatGatewayId == chatGatewayId).Device;
                        var tyntecURL = ctx.Configurations.FirstOrDefault(x => x.ConfigurationName == "ServLinkTyntecBaseAddress")?.ConfigurationValue;
                        mediaUrl = string.IsNullOrEmpty(tyntecURL) ? tyntecdMediaUrl : tyntecURL.TrimEnd('/') + "/media/";
                        Regex jsonRgx = new Regex(@"({(.*)})", RegexOptions.Singleline); //Get everything between first and last { }
                        var str = jsonRgx.Match(chatGatewayDevice).ToString();
                        TyntecConnectionInfo tyntecConnection = new TyntecConnectionInfo();
                        if(chatGatewayDevice.StartsWith("WhatsApp")) {
                            tyntecConnection = JsonConvert.DeserializeObject<TyntecConnectionInfo>(str);
                            mediaUrl = tyntecdMediaUrl + mediaId;
                        }

                        if(tyntecConnection == null) {
                            Dbg.WriteLine($"ApiKey not found - couldn't find ChatGateway connected to Chat or the Device was not properly set", "ChatImageController.ChatImage");
                            return null;
                        }
                        var apiKey = tyntecConnection.ApiKey;
                        if(!string.IsNullOrEmpty(apiKey)) {
                            var imgHttpResponse = DownloadChatImage(apiKey, mediaUrl);
                            if(imgHttpResponse != null) {
                                byte[] imgBin = imgHttpResponse.Content.ReadAsByteArrayAsync().Result;
                                imgHttpResponse.Content.Headers.ContentDisposition.DispositionType = DispositionTypeNames.Inline;
                                var imgData = new ImageDataPoco() {
                                    Data = imgBin,
                                    Dispo = imgHttpResponse.Content.Headers.ContentDisposition,
                                    ContentType = imgHttpResponse.Content.Headers.ContentType.ToString()
                                };
                                SocialMediaImageCache.Set(key, imgData);
                                return SendToBrowser(imgData);
                            }
                            else {
                                throw new Exception($"Tyntec mediaUrl response: {imgHttpResponse}");
                            }
                        }
                        else {
                            Dbg.WriteLine($"ApiKey not found", "ChatImageController.ChatImage");
                            return null;
                        }
                    }
                }
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown {e}", "ChatImageController.ChatImage");
                return null;
            }
        }

        public static HttpResponseMessage DownloadChatImage(string tyntecApiKey, string mediaUrl) {
            Dbg.WriteLine($"Downloading from Tyntec {mediaUrl}", "ChatImageController.ChatImage");
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("apiKey", tyntecApiKey);

            using(var r = client.GetAsync(mediaUrl)) {
                return r.Result;
            }
        }

#if V4CORE
        private IActionResult SendToBrowser(ImageDataPoco imgData) {
#else
        private IHttpActionResult SendToBrowser(ImageDataPoco imgData) {
#endif
#if V4CORE
            return File(imgData.Data, imgData.ContentType, imgData.Dispo.FileName, true);
#else
            return new BinaryFileResult(imgData.Data, imgData.Dispo, imgData.ContentType);
#endif
        }
    }

    class ImageDataPoco
    {
        public ContentDispositionHeaderValue Dispo { get; set; }
        public byte[] Data { get; set; }
        public string ContentType { get; set; }
    }
}
