//2021-2023

using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Net;
using System.Net.Http.Json;

using HexCorp.General.Common;
using HexCorp.General.Model.Common;

using WhatsApp;
using Microsoft.AspNetCore.Http;


namespace HexCorp.CommLink
{
    //WHATSAPP CONNECTOR
    //V2 Will be supported until 4/23

    //Manual webhooks setting through self service API
    //PATCH https://api.WhatsApp.com/conversations/v3/configurations/callbacks - updates the default application
    //https://www.WhatsApp.com/docs/whatsapp-business-api-self-service#toc--how-to-specify-applications-with-webhooks-

    /*
    {
        "callbackVersion": "2.11",
        "inboundMessageUrl": "<your server>",
        "messageStatusUrl": "<your server>",
        "eventFilter": [

            "MessageStatus::accepted",
            "MessageStatus::channelFailed",
            "MessageStatus::deleted",
            "MessageStatus::delivered",
            "MessageStatus::failed",
            "MessageStatus::seen"
        ]
    }
    */

    // udpated documentation https://api.WhatsApp.com/reference/conversations/current.html?javascript#whatsapp-Send%20a%20message
    // updated documentation https://api.WhatsApp.com/reference/conversations/current.html?javascript#whatsapp-messagerequest

    public class WhatsAppConnector : WebHook
    {

        public WhatsAppConnector(WhatsAppConfig WhatsAppConfig, LinkProcessor linkprocessor) : base(WhatsAppConfig, linkprocessor) {
            config = WhatsAppConfig;
        }

        private static HttpClient _httpClient = new HttpClient(
            new SocketsHttpHandler {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15) // Solves DNS tracking - Recreate every 15 minutes
            });

        WhatsAppConfig config;

        List<WAChannelJson> Channels = new List<WAChannelJson>(); // By PhoneNumber

        public void SetChannels(List<WAChannelJson> channels) {
            Channels = channels;
        }

        public override IReadOnlyCollection<WAChannelJson> GetChannels => Channels;

        public override async void ProcessChatConfirm(ChatCommandInfo si) {
            var readMessageFound = whatsAppMsgLog.TryGetValue(si.ID, out var waSender);
            WhatsApp.SetMessageReadResponse response = null;
            if(readMessageFound) {
                response = await MarkMessageAsRead(waSender.ApiKey, config.BaseAddress, si.ID);
                whatsAppMsgLog.TryRemove(si.ID, out _);
                if(response == null) {
                    Dbg.WriteLine($"Response from PUT MessageRead returned null - message will not be marked as read on the client's side", "WhatsAppConnector.ProcessChatConfirm");
                }
                else if(response.status != "200") {
                    //WhatsApp returns 404 for messages that have already been marked as read
                    Dbg.WriteLine($"WhatsApp message read response returned an error {response.status} ; {response.title} ; {response.detail}", "WhatsAppConnector.ProcessChatConfirm");
                }
                else {
                    CleanMessageBuffers(waSender.ToPhoneNumber, waSender.Sender, waSender.TimeStamp);
                }
            }
            else {
                Dbg.WriteLine($"Confirmed message not found in dictionary log {si.ID}", "WhatsAppConnector.ProcessChatConfirm");
            }
        }

        void CleanMessageBuffers(string toPhoneNumber, string senderName, DateTime timeSent) {
            try {
                Dbg.WriteLine($"Starting CleanMessageBuffers {toPhoneNumber} ; {senderName} ; {timeSent}", "WhatsAppConnector.CleanMessageBuffers");
                var phoeNrInDictionary = whatsAppMessageBuffer.TryGetValue(toPhoneNumber, out var phoneNrBuffer);
                if(phoeNrInDictionary) {
                    var msgBufferFound = phoneNrBuffer.TryGetValue(senderName, out var messageBuffer);
                    var messageIdList = messageBuffer.FindAll(x => x.Item2 <= timeSent).Select(id => id.Item1).ToList();
                    foreach(var msgId in messageIdList) {
                        whatsAppMsgLog.TryRemove(msgId, out _);
                    }
                }
                else {
                    Dbg.WriteLine($"Couldn't find message buffer for {toPhoneNumber}", "WhatsAppConnector.CleanMessageBuffers");
                }
            }
            catch(Exception e) {
                Dbg.WriteLine($"ERROR while cleaning WA message buffers: {e}", "WhatsAppConnector.CleanMessageBuffers");
            }
        }

        void AddMessageToBuffer(string toPhoneNumber, WhatsApp.WhatsAppInboundMessage message, DateTime dt) {
            try {
                Dbg.WriteLine($"Starting AddMessageToBuffer {toPhoneNumber} ; {message.from}", "WhatsAppConnector.AddMessageToBuffer");
                var phoeNrInDictionary = whatsAppMessageBuffer.TryGetValue(toPhoneNumber, out var phoneNrBuffer);
                if(phoeNrInDictionary) {
                    Dbg.WriteLine($"WhatsApp phone number found - phone numbers buffered {whatsAppMessageBuffer.Count}", "WhatsAppConnector.AddMessageToBuffer");
                    var msgBufferFound = phoneNrBuffer.TryGetValue(message.from, out var messageBuffer);
                    if(msgBufferFound) {
                        if(Dbg.App) Dbg.WriteLine($"Users buffer for given PhoneNumber {phoneNrBuffer.Count}", "WhatsAppConnector.AddMessageToBuffer");
                        messageBuffer.Add(new Tuple<string, DateTime>(message.messageId, dt));
                        phoneNrBuffer[message.from] = messageBuffer;
                        whatsAppMessageBuffer[toPhoneNumber] = phoneNrBuffer;
                    }
                    else {
                        phoneNrBuffer.Add(toPhoneNumber, new List<Tuple<string, DateTime>>() {
                            new Tuple<string, DateTime>(message.messageId, dt)
                        });
                    }
                }
                else {
                    Dbg.WriteLine($"Adding WhatsApp PhoneNumber to buffer", "WhatsAppConnector.AddMessageToBuffer");
                    whatsAppMessageBuffer[toPhoneNumber] = new Dictionary<string, List<Tuple<string, DateTime>>>() {
                        {
                            message.from, new List<Tuple<string, DateTime>>(){
                                new Tuple<string, DateTime>(message.messageId, dt)
                            }
                        }
                    };
                }
            }
            catch(Exception e) {
                Dbg.WriteLine($"ERROR while adding to WA message buffers: {e}", "WhatsAppConnector.AddMessageToBuffer");
            }
        }

        public override void ProcessChatSentence(ChatSentenceInfo si) {
            Dbg.WriteLine($"WhatsApp: Port {si.Port}", "WhatsAppConnector.ProcessChatSentence");
            var intgr = Channels.Find(f => f.PeerPort == si.Port);
            //Is dictionary with contacts gonna be needed here? - probably not, but who knows
            //InitDictionaryOfAmioContactsAndChannels(selectedChannels.Select(sch => sch.Id).ToArray()); //?????????
            if(intgr == null) {
                Dbg.WriteLine("Warning", $"WhatsApp: Port '{si.Port}' is not active", "WhatsAppConnector.ProcessChatSentence");
                return;
            }
            SendWhatsAppMessage(si);
        }

        private void SendWhatsAppMessage(ChatSentenceInfo si) {
            if(Dbg.Detail) Dbg.WriteLine($"Starting SendWhatsAppMessage ID {si.Port}; Text {si.Text}; PartyName {si.PartyName}", "WhatsAppConnector.SendWhatsAppMessage");
            var WhatsAppChannel = Channels.Find(p => p.PeerPort == si.Port);
            if(Dbg.Detail) Dbg.WriteLine($"TrackingID {si.TrackingId}", "WhatsAppConnector.SendWhatsAppMessage");
            var trackingIdSplit = si.TrackingId.Split('_');
            var realUserId = trackingIdSplit[trackingIdSplit.Length-1]; //RealUserID contains "from" phone number at the end
            if(Dbg.Detail) Dbg.WriteLine($"Message to {realUserId}", "WhatsAppConnector.SendWhatsAppMessage");
            var result = WhatsAppConnector.SendMessage(WhatsAppChannel.ApiKey, config.BaseAddress, WhatsAppChannel.PhoneNumber, realUserId, si.Text).Result;
            if(result != null && result.status == null) {
                processor.ChatConfirm(si.TrackingId, si.Port, si.GatewayId, si.ID);
            }
        }

        public override async Task ProcessRequest(HttpContext context) {
            try {
                CheckHookReady();
                CheckJsonContentType(context); 
                var message = await context.Request.ReadFromJsonAsync<WhatsAppInboundMessage>();
                if(message != null) {
                    if(message.@event != null && message.@event.Contains("MessageStatus")) {
                        if(Dbg.Detail) Dbg.WriteLine($"Received 'MessageStatus' message - ignoring and returning '200 OK'", "WhatsAppConnector.ProcessRequest");
                        await CreateResponse(context, HttpStatusCode.OK, ""); 
                        return;
                    }
                    else if(Dbg.Detail) Dbg.WriteLine($"Message received {JsonAdapter.SerializeObject(message)}", "WhatsAppConnector.ProcessRequest");
                    var api = Channels.Find(ch => ch.PhoneNumber.EndsWith(message.to)); //The EndsWith allows for the country code '+' to be taken out of the equation
                    if(api != null) {
                        if(Dbg.Detail) Dbg.WriteLine($"Api found - phone number {api.PhoneNumber}", "WhatsAppConnector.ProcessRequest");
                        if(message.channel.Equals("whatsapp") && message.@event.Equals("MoMessage")) {
                            if(message.content.contentType != null && message.content.contentType == "location") {
                                if(Dbg.Detail) Dbg.WriteLine($"WhatsApp contentType = location, skipping", "WhatsAppConnector.ProcessRequest");
                                await CreateResponse(context, HttpStatusCode.OK, "");
                                return;
                            }
                            //Aktualne pro prehlednost
                            var trackingId = $"{WAChannelJson.Prefix}_{message.to}_{message.from}"; //WhatsApp part can be omitted, but it's easily recognizable in logs, however pairing the TO/FROM numbers is important as the same FROM number can potentially reach out through multiple channels/lines, therefore the number alone IS NOT a unique trackingId
                            var remoteAddress = processor.NormalizePhoneNumber(message.from);
                            var partyName = message.whatsapp.senderName;
                            var txtContext = new string[2]; //string[2] - non live chats need to store a possible image caption
                            if(Dbg.Detail) Dbg.WriteLine($"trackingId {trackingId}", "WhatsAppConnector.ProcessRequest");
                            var liveChat = LivingChats.TryGetValue(trackingId, out var currentChat);
                            if(!liveChat) {
                                if(Dbg.Detail) Dbg.WriteLine($"WhatsApp Chat is not Live - checking for Signal", "WhatsAppConnector.ProcessRequest");
                                processor.ChatRequestForSignal(trackingId, api.GatewayId);
                                if(await AwaitResponse(trackingId)) {
                                    var signalExists = SignalDictionary.TryGetValue(trackingId, out var signalInfo);
                                    if(signalExists && !string.IsNullOrEmpty(signalInfo.ChatBannerMessage) && !signalInfo.Signal.ToLower().Equals(signalInfo.ChatBannerMessage.ToLower())) {
                                        txtContext = await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName, chatIsLive: false);
                                        if(Dbg.App) Dbg.WriteLine($"WhatsApp txtContext {txtContext[0]} || {txtContext[1]}", "WhatsAppConnector.ProcessRequest");
                                        if((signalInfo.Signal.ToLower().Equals(ChatGateCondition_Signal.Free.ToLower()) || signalInfo.Signal.ToLower().Equals(ChatGateCondition_Signal.Busy.ToLower()))) {
                                            if(api.InstantBannerMessages) {
                                                if(Dbg.Detail) Dbg.WriteLine($"InstantBannerMessages {api.PhoneNumber} set to true - ChatStart - Signal = {signalInfo.Signal}", "WhatsAppConnector.ProcessRequest");
                                                processor.ChatStart(trackingId, api.PeerPort, api.GatewayId, remoteAddress, partyName);
                                                if(!string.IsNullOrEmpty(txtContext[1])) {
                                                    if(Dbg.Detail) Dbg.WriteLine($"Message includes an image or a video {message.content.media.type}", "WhatsAppConnector.ProcessRequest");
                                                    SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, txtContext[1]);
                                                    var msgImageContent = (await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName, partialUrl: txtContext[0], isPartialUrl: true))[0];
                                                    SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, msgImageContent, messageType: message.content.media?.type);
                                                }
                                                else {
                                                    SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, txtContext[0]);
                                                }
                                                SendWhatsAppMessage(new ChatSentenceInfo { PartyName = partyName, Port = api.PeerPort, GatewayId = api.GatewayId, TrackingId = trackingId, Text = signalInfo.ChatBannerMessage, ID = message.messageId });
                                                SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, signalInfo.ChatBannerMessage, chatSentenceInfo: "ICR");
                                                if(Dbg.Detail) Dbg.WriteLine($"Chat started and all the messages have been sent", "WhatsAppConnector.ProcessRequest");
                                            }
                                            else {
                                                //Creates or gets and updates an instance of incoming chat depending on PSID
                                                //If add => check if the first message was and img, if so, add both txtContext lines, else just one
                                                //If update, do basically the same thing
                                                var incomingChat = IncomingChatsDictionary.AddOrUpdate(remoteAddress, !string.IsNullOrEmpty(txtContext[1]) ?
                                                        new IncomingChatPoco() { IncomingMessages = new List<ChatMessage>() { new ChatMessage() { Message = txtContext[1] }, new ChatMessage() { Message = txtContext[0] } } } :
                                                        new IncomingChatPoco() { IncomingMessages = new List<ChatMessage>() { new ChatMessage() { Message = txtContext[0] } } },
                                                            (k, v) => {
                                                                if(!string.IsNullOrEmpty(txtContext[1])) v.IncomingMessages.Add(new ChatMessage() { Message = txtContext[1] });
                                                                v.IncomingMessages.Add(new ChatMessage() { Message = txtContext[0] });
                                                                return v;
                                                            });
                                                if(!string.IsNullOrEmpty(incomingChat.BannerMessage)) {
                                                    if(Dbg.Detail) Dbg.WriteLine($"Calling ChatStart - Signal = {signalInfo.Signal}", "WhatsAppConnector.ProcessRequest");
                                                    processor.ChatStart(trackingId, api.PeerPort, api.GatewayId, remoteAddress, partyName);
                                                    if(Dbg.Detail) Dbg.WriteLine($"Removing {remoteAddress} from IncomingChatsDictionary", "WhatsAppConnector.ProcessRequest");
                                                    IncomingChatsDictionary.TryRemove(remoteAddress, out var incChat);
                                                    if(Dbg.Detail) Dbg.WriteLine($"Calling ChatSentence for {incChat.IncomingMessages.Count} messages from incoming chat", "WhatsAppConnector.ProcessRequest");
                                                    for(int i = 0; i < incChat.IncomingMessages.Count; i++) {
                                                        if(i == 1) SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, incChat.BannerMessage, chatSentenceInfo: "ICR");
                                                        //Message used for both msg text and stickerId - the CheckChatSentence method can deal with that depending on messageType
                                                        if(incChat.IncomingMessages[i].Message.Contains(subst)) incChat.IncomingMessages[i].Message = (await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName, partialUrl: incChat.IncomingMessages[0].Message, isPartialUrl: true))[0];
                                                        SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, incChat.IncomingMessages[i].Message, messageType: message.content.media?.type);
                                                    }
                                                }
                                                else {
                                                    if(Dbg.Detail) Dbg.WriteLine($"Calling ChatStart - Signal = {signalInfo.Signal}, sending banner message {signalInfo.ChatBannerMessage}", "WhatsAppConnector.ProcessRequest");
                                                    SendWhatsAppMessage(new ChatSentenceInfo { PartyName = partyName, Port = api.PeerPort, GatewayId = api.GatewayId, TrackingId = trackingId, Text = signalInfo.ChatBannerMessage, ID = message.messageId });
                                                    incomingChat.BannerMessage = signalInfo.ChatBannerMessage;
                                                }
                                            }
                                        }
                                        else {
                                            if(Dbg.Detail) Dbg.WriteLine($"Calling ChatSentence - Signal = {signalInfo.Signal}, applying banner message {signalInfo.ChatBannerMessage}", "WhatsAppConnector.ProcessRequest");
                                            SendWhatsAppMessage(new ChatSentenceInfo { PartyName = partyName, Port = api.PeerPort, GatewayId = api.GatewayId, TrackingId = trackingId, Text = signalInfo.ChatBannerMessage, ID = message.messageId });
                                        }
                                    }
                                    else {
                                        if(Dbg.Detail) Dbg.WriteLine($"Signal for WhatsApp port {api.PeerPort} not found or ChatBannerMessage is empty, starting chat", "WhatsAppConnector.ProcessRequest");
                                        processor.ChatStart(trackingId, api.PeerPort, api.GatewayId, remoteAddress, partyName);
                                        txtContext = await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName);
                                        if(Dbg.Detail) Dbg.WriteLine($"Calling ChatSentence", "WhatsAppConnector.ProcessRequest");
                                        SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, txtContext[0]);
                                    }
                                }
                                else {
                                    if(Dbg.Detail) Dbg.WriteLine($"AwaitResponse TIMEOUT - regular chat start/chat sentence", "WhatsAppConnector.ProcessRequest");
                                    processor.ChatStart(trackingId, api.PeerPort, api.GatewayId, remoteAddress, partyName);
                                    txtContext = await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName);
                                    if(Dbg.Detail) Dbg.WriteLine($"Calling ChatSentence", "WhatsAppConnector.ProcessRequest");
                                    SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, txtContext[0], messageType: message.content.media?.type);
                                }
                            }
                            else {
                                txtContext = await HandleWhatsAppMessageContent(message, trackingId, api.PeerPort, api.GatewayId, partyName);
                                if(Dbg.Detail) Dbg.WriteLine($"WhatsApp Chat is Live {currentChat.ChatId}", "WhatsAppConnector.ProcessRequest");
                                if(Dbg.Detail) Dbg.WriteLine("Calling regular ChatSentence", "WhatsAppConnector.ProcessRequest");
                                SendChatSentence(trackingId, api.PeerPort, api.GatewayId, message.messageId, partyName, txtContext[0], messageType: message.content.media?.type);
                            }
                            var dt = DateTime.TryParse(message.receivedAt, out var dateTime);
                            Dbg.WriteLine($"Parsed date success {dt} - {dateTime}", "WhatsAppConnector.ProcessRequest");
                            whatsAppMsgLog[message.messageId] = new WhatsAppSenderInfo() { ApiKey = api.ApiKey, Sender = message.from, ToPhoneNumber = message.to, ReceivedAt = message.receivedAt, TimeStamp = dateTime };
                            AddMessageToBuffer(api.PeerPort, message, dateTime);
                        }
                    }
                    else {
                        if(Dbg.Detail) Dbg.WriteLine($"MESSAGE LOST - no Api found - PhoneNumber to {message.to} ; from {message.from} ; message {message.content?.text}", "WhatsAppConnector.ProcessRequest");
                    }
                }
                await CreateResponse(context, HttpStatusCode.OK, "");
            }
            catch(WebHookException whe) {
                Dbg.WriteLine($"Error occured while processing request for {this}", whe, "WhatsAppConnector.ProcessRequest");
                await CreateResponse(context, whe.StatusCode, whe.Message);
            }
            catch(Exception e) {
                Dbg.WriteLine($"Error occured while processing request for {this}", e, "WhatsAppConnector.ProcessRequest");
                await CreateResponse(context, HttpStatusCode.InternalServerError, $"Error while processing data: {e.Message}");
            }
        }

        const string subst = "<subst>";
        //Placed here instaed of a WhatsAppConnector to avoid changes in protection levels
        async Task<string[]> HandleWhatsAppMessageContent(WhatsApp.WhatsAppInboundMessage message, string trackingId, string port, Guid gwId, string partyName, string partialUrl = "", bool isPartialUrl = false, bool chatIsLive = true) {
            if(Dbg.App) Dbg.WriteLine($"Starting HandleWhatsAppMessageContent", "WhatsAppConnector.HandleWhatsAppMessageContent");
            var txtContext = new string[2];
            if(isPartialUrl && !string.IsNullOrEmpty(partialUrl)) {
                if(Dbg.App) Dbg.WriteLine($"isPartialUrl true {partialUrl}", "WhatsAppConnector.HandleWhatsAppMessageContent");
                var liveChat = LivingChats.TryGetValue(trackingId, out var currentChat);
                if(liveChat) txtContext[0] = partialUrl.Replace(subst, currentChat.ChatId.ToString());
                if(Dbg.App) Dbg.WriteLine($"Returning full url {txtContext[0]}", "WhatsAppConnector.HandleWhatsAppMessageContent");
                return txtContext;
            }
            if(message.content.contentType.Equals("media") && !string.IsNullOrEmpty(message.content.media.url)) {
                if(message.content.media.type.Equals("image") || message.content.media.type.Equals("sticker") || message.content.media.type.Equals("video")) {
                    if(Dbg.App) Dbg.WriteLine("WhatsApp message.content.media.type = image", "WhatsAppConnector.HandleWhatsAppMessageContent");
                    // /<BaseUrl>/api/ChatImage/236aa5c3-e776-4579-80b4-ebcae64b7eeb/?dkokp45d84ekedkopk.jpg
                    //baseImgUrl is expected to be an URL of RC location
                    var baseImgUrl = processor.AgentClientBaseUrl.EndsWith("/") ? processor.AgentClientBaseUrl : processor.AgentClientBaseUrl + "/"; //Safeguard against double slash '//'
                                                                                                                                                     // IN CASE OF CHANGE:  'api/ChatImage/' is a hardcoded part, located in RC.Controllers.ChatImageController - RoutePrefix
                    if(Dbg.Detail) Dbg.WriteLine($"Base Image URL {baseImgUrl}", "WhatsAppConnector.HandleWhatsAppMessageContent");

                    if(chatIsLive) {
                        //Await needed because it often happened that the StartChat didn't process in time before calling the ChatSentence method
                        if(await AwaitResponseLiveChat(trackingId)) {
                            LivingChats.TryGetValue(trackingId, out var currentChat);
                            txtContext[0] = baseImgUrl + "api/ChatImage/" + currentChat.ChatId + "/" + message.content.media.mediaId;
                            if(Dbg.Detail) Dbg.WriteLine($"Final Image Media URL {txtContext[0]}", "WhatsAppConnector.HandleWhatsAppMessageContent");
                        }
                        else {
                            txtContext[0] = baseImgUrl + "api/ChatImage/" + subst + "/" + message.content.media.mediaId;
                            if(Dbg.Detail) Dbg.WriteLine($"TIMEOUT for Image Media {txtContext[0]}", "WhatsAppConnector.HandleWhatsAppMessageContent");
                        }
                    }
                    else {
                        txtContext[0] = baseImgUrl + "api/ChatImage/" + subst + "/" + message.content.media.mediaId;
                        if(Dbg.Detail) Dbg.WriteLine($"Partial Image Media URL {txtContext[0]}", "WhatsAppConnector.HandleWhatsAppMessageContent");
                    }

                    //If caption not null -> there was a text alongside the image - needs to be sent as a separate chat sentence
                    if(!string.IsNullOrEmpty(message.content.media.caption)) {
                        if(chatIsLive) SendChatSentence(trackingId, port, gwId, message.messageId, partyName, message.content.media.caption);
                        else txtContext[1] = message.content.media.caption;
                    }
                }
                if(message.content.media.type.Equals("document")) {
                    if(Dbg.App) Dbg.WriteLine("WhatsApp message.content.media.type = document", "WhatsAppConnector.HandleWhatsAppMessageContent");
                    txtContext[0] = "##WhatsAppFile##";
                }
            }
            else {
                txtContext[0] = message.content.contentType.Equals("text") && message.content.text != null ? message.content.text : (
                message.content.media != null ? message.content.media.url : null);
            }
            return txtContext;
        }


        ConcurrentDictionary<string, WhatsAppSenderInfo> whatsAppMsgLog = new ConcurrentDictionary<string, WhatsApp.WhatsAppSenderInfo>(); //<msgId, WaSenderDetails>
        ConcurrentDictionary<string, Dictionary<string, List<Tuple<string, DateTime>>>> whatsAppMessageBuffer = new ConcurrentDictionary<string, Dictionary<string, List<Tuple<string, DateTime>>>>(); //<WaPhoneNumber, <fromPhoneNumber, Object(messageId, timeStamp)>>

        ConcurrentDictionary<string, IncomingChatPoco> IncomingChatsDictionary = new ConcurrentDictionary<string, IncomingChatPoco>(); //<senderId, IncomingMessages>

        void SendChatSentence(string trackingId, string port, Guid gwId, string ID, string partyName, string text, string messageType = "", string chatSentenceInfo = "") {
            var sentenceModel = ChatSentenceInfo.M_Sentence;
            if(Dbg.Detail) Dbg.WriteLine($"Starting ChatSentence with chatSentenceInfo {chatSentenceInfo}, messagetype {messageType}", "WhatsAppConnector.SendChatSentence");
            //A clear indicator that we're sending an ICR message - no need to check the sentence, it's hardcoded and works with DB input
            if(chatSentenceInfo.Equals("ICR")) {
                //Will be later changed when a separate channel is created for ICR responses
                sentenceModel = ChatSentenceInfo.M_TechNote;
                text = "ICR: " + text; //Can be deleted once a separate channel is created
            }
            else {
                //In case the message is not a simple text, run image/url checks
                try {
                    if(htmlTagRgx.IsMatch(text) || Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute)) {
                        //CheckChatSentence returns a Tuple<string, int> = Tuple<messageText,ChatSentenceInfo>
                        var textAndSentenceTypeTuple = CheckChatSentence(text, messageType); //Checks for text contents - URL for images, files from (un)safe URIs or potentially dangerous HTML code
                        text = textAndSentenceTypeTuple.Item1;
                        sentenceModel = textAndSentenceTypeTuple.Item2; //M_Sentence would filter the html tags, however M_TechNote won't save the text into Chat.BodyText
                        if(Dbg.Detail) Dbg.WriteLine($"ChatSentenceInfo changed to {sentenceModel}", "WhatsAppConnector.SendChatSentence");
                        if(Dbg.Detail) Dbg.WriteLine($"Returned text {text}", "WhatsAppConnector.SendChatSentence");
                    }
                }
                catch(UriFormatException) {
                    //Catches the single word message sentence info type problem
                    if(Dbg.Detail) Dbg.WriteLine($"UriFormatException for text {text}, returning as Sentence", "WhatsAppConnector.SendChatSentence");
                }
                catch(Exception e) {
                    if(Dbg.Detail) Dbg.WriteLine($"Exception thrown {e} --- for text {text}", "WhatsAppConnector.SendChatSentence");
                    sentenceModel = ChatSentenceInfo.M_TechNote; //M_Sentence would filter the html tags, however M_TechNote won't save the text into Chat.BodyText
                }
            }
            processor.SendChatSentence(trackingId, port, gwId, ID, partyName, sentenceModel, text);
        }

        Tuple<string, int> CheckChatSentence(string text, string messageType) {
            if(Dbg.Detail) Dbg.WriteLine("Starting CheckChatSentence", "WhatsAppConnector.CheckChatSentence");
            bool validUri = false;
            try {
                validUri = Uri.IsWellFormedUriString(text, UriKind.RelativeOrAbsolute);
            }
            catch(UriFormatException) {
                if(Dbg.Detail) Dbg.WriteLine($"Failed on validUri check - {text}, setting ValidURI to false", "WhatsAppConnector.CheckChatSentence");
                validUri = false;
            }

            if(Dbg.Detail) Dbg.WriteLine($"htmlTagRgx - {htmlTagRgx.IsMatch(text)}", "WhatsAppConnector.CheckChatSentence");
            if(htmlTagRgx.IsMatch(text)) {
                return new Tuple<string, int>($"<span style=\"color: red; font-weight:bold\">[##HarmfulCode## - {htmlTagRgx.Matches(text).Count} <..> ##Matches##!]</span><br>{text.Replace("<", "&lt").Replace(">", "&gt")}", ChatSentenceInfo.M_TechNote);
            }
            string uriHost = "placeholder"; //The text is there to prevent a mix up in case someone put in an empty string among the safe uris by mistake - processor.SafeUris.Any(s => uriHost.ToLower().Equals(s))
            try {
                uriHost = new Uri(text).Host.ToLower();
                uriHost = uriHost.StartsWith("www.") ? uriHost.Remove(0, 4) : uriHost;
                if(Dbg.Detail) Console.WriteLine($"URL {text} - Uri.Host - {uriHost}", "WhatsAppConnector.CheckChatSentence");
            }
            catch(UriFormatException) {
                //Can happen for the current format of WA images /AgentClient/api/ChatImage/xxx/zzz
                if(Dbg.Detail) Console.WriteLine($"Can't extract UriHost from {text}", "WhatsAppConnector.CheckChatSentence");
            }
            if(validUri) {
                if(text.StartsWith(processor.AgentClientBaseUrl)) {
                    if(messageType == "video") {
                        if(Dbg.Detail) Dbg.WriteLine($"WhatsApp - Replacing text with <video> tag - {text}", "WhatsAppConnector.CheckChatSentence");
                        //return new Tuple<string, int>($"<a target=\"_blank\" rel=\"noopener noreferrer\" href={text}><video autoplay muted controls style=\"width:150px\" src={text} alt={text} /></a>", ChatSentenceInfo.M_TechNote);
                        return new Tuple<string, int>($"<a target=\"_blank\" rel=\"noopener noreferrer\" href={text}><video autoplay muted controls style=\"width:150px\" alt={text}><source src={text} type=\"video/mp4\"><source src={text} type=\"video/webm\"></video></a>", ChatSentenceInfo.M_TechNote);
                    }
                    else {
                        if(Dbg.Detail) Dbg.WriteLine($"WhatsApp - Replacing text with <img> tag - {text}", "WhatsAppConnector.CheckChatSentence");
                        return new Tuple<string, int>($"<a target=\"_blank\" rel=\"noopener noreferrer\" href={text}><img style=\"width:150px\" src={text} alt={text} /></a>", ChatSentenceInfo.M_TechNote);
                    }
                }
                else if(processor.SafeUris.Any(s => uriHost.ToLower().Equals(s))) {
                    if(Dbg.Detail) Dbg.WriteLine($"WhatsApp potential hamrful URL, URI not the same as the host srv in WebhooksConfig - {text}", "WhatsAppConnector.CheckChatSentence");
                    return new Tuple<string, int>($"<span style=\"color: red; font-weight:bold\">[##HarmfulLink##!]</span><br>{text}", ChatSentenceInfo.M_TechNote);
                }
                else {
                    if(Dbg.Detail) Dbg.WriteLine($"Probably a one word message - returning as sentence {text}", "WhatsAppConnector.CheckChatSentence");
                }
            }
            if(Dbg.Detail) Dbg.WriteLine($"No condition met, returning pure text as Sentence", "WhatsAppConnector.CheckChatSentence");
            return new Tuple<string, int>(text, ChatSentenceInfo.M_Sentence);
        }

        async static Task<WhatsAppSendMsgResponse> SendMessage(string WhatsAppApiKey, string baseAddress, string fromPhoneNr, string toPhoneNr, string text) {
            //https://api.WhatsApp.com/reference/conversations/current.html?shell#whatsapp-Send%20a%20message
            if(Dbg.Detail) Dbg.WriteLine($"Starting SendMessage to nr {toPhoneNr} -> text: {text}", "WhatsAppConnector.SendMessage");
            var url = baseAddress + "/messages";

            //As for now, it should be enough to have the text ContentType - the moment we allow sending images and files, it'll need an expansion
            var postWhatsAppData = new WhatsAppOutboundMessage()
            {
                to = toPhoneNr,
                from = fromPhoneNr.TrimStart('+'), //V3 doesn't support the '+' sign, but seems to be working without it
                content = new WhatsAppMsgBody()
                {
                    contentType = "text",
                    text = text
                }
            };
            //If we ever decide we can send images and files back
            //if(media){
            //if(Dbg.Detail) Dbg.WriteLine($"Outgoing message has media", "WhatsAppConnector.SendMessage");
            //    postWhatsAppData.whatsapp.contentType = ;
            //    postWhatsAppData.whatsapp.url = ;
            //    postWhatsAppData.whatsapp.type = ;
            //    postWhatsAppData.whatsapp.media = new WhatsAppMedia() {

            //    };
            // }
            try {
                if(Dbg.Inits) Dbg.WriteLine($"Force setting SecurityProtocol to TLS1.2", "WhatsAppConnector.SendMessage");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Post,
                    Content = JsonContent.Create(postWhatsAppData)
                };
                request.Headers.Add("apiKey", WhatsAppApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if(Dbg.Detail) Dbg.WriteLine($"Client has been set up, sending message to {url}", "WhatsAppConnector.SendMessage");
                if(Dbg.Detail) Dbg.WriteLine($"Sending data {JsonAdapter.SerializeObject(postWhatsAppData)}", "WhatsAppConnector.SendMessage");

                using(var r = _httpClient.SendAsync(request)) {
                    string result = await r.Result.Content.ReadAsStringAsync();
                    if(Dbg.Detail) Dbg.WriteLine($"API returned {(int)r.Result.StatusCode} : {result}", "WhatsAppConnector.SendMessage");
                    return JsonAdapter.DeserializeObject<WhatsAppSendMsgResponse>(result);
                }
            }
            catch(Exception e) {
                if(Dbg.Detail) Dbg.WriteLine($"Exception thrown from WhatsAppMSG {e.InnerException}", "WhatsAppConnector.SendMessage");
                if(Dbg.Detail) Dbg.WriteLine($"Debug from aggregate", e, "WhatsAppConnector.SendMessage");
                throw new Exception(e.InnerException.ToString());
            }
        }

        //PUT /channels/whatsapp/messages/{message-id}
        public async static Task<SetMessageReadResponse> MarkMessageAsRead(string WhatsAppApiKey, string baseAddress, string messageId) {
            var url = baseAddress + $"/channels/whatsapp/messages/{messageId}";

            //As for now, it should be enough to have the text ContentType - the moment we allow sending images and files, it'll need an expansion
            var postWhatsAppMsgRead = new SetMessageRead()
            {
                status = "read"
            };

            try {
                if(Dbg.Inits) Dbg.WriteLine($"Force setting SecurityProtocol to TLS1.2", "WhatsAppConnector.MarkMessageAsRead");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Put,
                    Content = JsonContent.Create(postWhatsAppMsgRead)
                };
                request.Headers.Add("apiKey", WhatsAppApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if(Dbg.Detail) Dbg.WriteLine($"Client has been set up, sending message read to {url}", "WhatsAppConnector.MarkMessageAsRead");
                using(var r = _httpClient.SendAsync(request)) {
                    string result = await r.Result.Content.ReadAsStringAsync();
                    if(Dbg.Detail) Dbg.WriteLine($"API returned {result}", "WhatsAppConnector.MarkMessageAsRead");
                    if(string.IsNullOrEmpty(result)) {
                        if(Dbg.Detail) Dbg.WriteLine($"Returned status code {(int)r.Result.StatusCode}", "WhatsAppConnector.MarkMessageAsRead");
                        return (new SetMessageReadResponse()
                        {
                            status = ((int)r.Result.StatusCode).ToString(),
                            detail = r.Result.ReasonPhrase.ToString()
                        });
                    }
                    return JsonAdapter.DeserializeObject<SetMessageReadResponse>(result);
                }
            }
            catch(Exception e) {
                if(Dbg.Detail) Dbg.WriteLine($"Exception thrown from message read {e.InnerException}", "WhatsAppConnector.MarkMessageAsRead");
                if(Dbg.Detail) Dbg.WriteLine($"Debug from aggregate, returning null", e, "WhatsAppConnector.MarkMessageAsRead");
                return null; //null is enough here, we don't want this to block the programme, so the debug log is sufficient
            }
        }
    }
}
