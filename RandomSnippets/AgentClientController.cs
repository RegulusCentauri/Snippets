//Oct-Dec 2019

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using HexCorp.General.Common;
using HexCorp.Model.Dbml;
using RO = HexCorp.Model.Dbml.RO;
using HexCorp.Model.DataStructures;
using HexCorp.App.RandomClient.Web.Models;
using HexCorp.Model.Security;
using HexCorp.Model.Common;

using Newtonsoft.Json;
using ExtensionMethods;

namespace HexCorp.App.RandomClient.Web.Controllers
{

    [Authorize]
    [RoutePrefix("api/React")]
    public class AgentClientController : ApiController
    {
        static ExpiringCache<CtcClientDetailPoco> CtcClientDetailsCache;
        static ExpiringCache<List<ClientNotesCategoryPoco>> ClientNotesCategoryCache;
        static Properties.Settings propsAttr = Properties.Settings.Default; //Properties attributes

        public const string RelatedMsgStatus_Sent = "SENT";
        public const string RelatedMsgStatus_Failed = "FAILED";
        public const string RelatedMsgStatus_Confirmed = "CONFIRMED";

        static AgentClientController() {
            //Value of RandomClientClientDetailCacheExpiration is stored inside the web.config/properties file and is hard set, not dynamic
            //Value is set in minutes
            var expiretoClientDetail = propsAttr.RandomClientClientDetailCacheExpiration;
            CtcClientDetailsCache = new ExpiringCache<CtcClientDetailPoco>(expiretoClientDetail * 60, "RandomClient.CtcGetClientDetail");
            var expiretoNoteCategories = propsAttr.RandomClientNotesCategoryCacheExpiration;
            ClientNotesCategoryCache = new ExpiringCache<List<ClientNotesCategoryPoco>>(expiretoNoteCategories * 60, "RandomClient.ClientNotesCategoryPoco");
        }

        //http://localhost:60600/AgentClient/pages/MessageEditor.html?id=cf7c98dd-4ec1-48f4-92a5-75bc38482a1c
        //Function for the remote system DB search
        [HttpPost, Route("search")]
        public CtcClientInfosPoco Search([FromBody] ContactSearchParams parameters) {
            Dbg.WriteLine($"Starting Search", "AgentClientController.Search");
            string brandName = "";
            try {
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.Search");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access",
                        });
                    }

                    var firmType = parameters.Firm.StringToIntThousands("add");
                    brandName = ctx.ContactModels.FirstOrDefault(x => x.Model.Equals(firmType))?.DisplayName;
                    if(string.IsNullOrEmpty(brandName)) {
                        Dbg.WriteLine($"There is no model {firmType.ToString()}", "IntegrationController.UpdateChat");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"There is no model {firmType.ToString()} - brandName not found"
                        });
                    }
                }
                //Debugging purposes
                int? clientIdConvert = null;
                DateTime birthdayConvert = new DateTime();
                try {
                    Dbg.WriteLine($"Starting clientId and Birthday date conversion", "AgentClientController.Search");
                    //Workaround to safely convert with regards to ClientInfoPoco types
                    if(!string.IsNullOrEmpty(parameters.ClientId)) {
                        clientIdConvert = Convert.ToInt32(parameters.ClientId);
                    }
                    //int? test = parameters.ClientId.Equals("") ? null : Convert.ToInt32(parameters.ClientId); //Ternary not working for nullable int
                    if(!string.IsNullOrEmpty(parameters.Birthday)) {
                        birthdayConvert = Convert.ToDateTime(parameters.Birthday);
                    }
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown {e} - ClientId - {clientIdConvert} /Birthday - {birthdayConvert} convert failed : ", "AgentClientController.Search");
                }
                Dbg.WriteLine($"Finishing clientId and Birthday date conversion", "AgentClientController.Search");

                CtcClientInfosPoco clientInfo = new CtcClientInfosPoco();
                //Debugging purposes
                //clientInfo = new CtcClientInfosPoco(searchType: "FULL", phoneNumber: parameters.Phone, email: parameters.Email, clientId: clientIdConvert, username: parameters.Username,
                //    firm: Convert.ToInt32(parameters.Firm), firstName: parameters.FirstName, lastName: parameters.LastName, birthday: birthdayConvert);

                Dbg.WriteLine($"Calling getClientInfo", "AgentClientController.Search");
                parameters.Email = parameters.Email?.Replace("@", "%40"); //@ is a QS symbol
                parameters.Phone = parameters.Phone?.Replace("+", "%2B"); //+ sign needs to be replaced with hexa - QueryString has a + sign for a space character and therefore doesn't encode it in the following function
                parameters.Username = parameters.Username?.Replace("+", "%2B");
                parameters.Username = parameters.Username?.Replace("@", "%40");
                clientInfo = GetWebClient<CtcClientInfosPoco>("/search", parameters.Firm, new List<string>() { "type", "FULL", "clientId", parameters.ClientId, "phoneNumber",
                    parameters.Phone, "clientEmail", parameters.Email, "clientUsername", parameters.Username, "brand", brandName, "clientFirstName", parameters.FirstName, "name",
                    parameters.LastName, "clientDateOfBirth", parameters.Birthday});
                return clientInfo;
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown {e}", "AgentClientController.Search");
                CtcClientInfosPoco clientInfo = new CtcClientInfosPoco();
                clientInfo.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(e.Response.StatusCode),
                    title = e.Response.StatusCode.ToString(),
                    detail = e.Response.ReasonPhrase + " - Exception: " + e
                };
                return clientInfo;
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown {e}", "AgentClientController.Search");
                CtcClientInfosPoco clientInfo = new CtcClientInfosPoco();
                clientInfo.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = "Exception: " + e.ToString()
                };
                return clientInfo;
            }
        }

        //Function to prefill the online form
        [HttpPost, Route("prefillsearch/{editormodel:maxlength(24)}/{recordid:guid}")]
        public CtcPrefilPoco PrefillSearch([FromUri] string editormodel, [FromUri] Guid recordid) {
            Dbg.WriteLine($"Starting PrefillSearch", "AgentClientController.PrefillSearch");
            CtcPrefilPoco prefil = new CtcPrefilPoco();
            using(var ctx = new DbReadOnlyContext()) {
                try {
                    AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.PrefillSearch");
                    throw new HttpResponseException(new HttpResponseMessage() {
                        StatusCode = HttpStatusCode.Unauthorized,
                        ReasonPhrase = $"Unauthorized access"
                    });
                }
            }
            using(var ctx = new DbDataContext()) {
                try {
                    switch(editormodel) {
                        case Model.Common.Editor_Model.CallEditor:
                        case Model.Common.Editor_Model.CallShelf:
                            var oc = ctx.OutboundCalls.Where(y => y.OutboundCallId == recordid).Select(y => new { y.CallerNumber, y.Contact.ContactModel.Model }).FirstOrDefault();
                            prefil.contactData = oc?.CallerNumber; //OC is more probable than inbound call
                            if(string.IsNullOrEmpty(prefil.contactData)) {
                                var ic = ctx.InboundCalls.Where(y => y.InboundCallId == recordid).Select(y => new { y.CallerNumber, y.Contact.ContactModel.Model }).FirstOrDefault();
                                prefil.contactData = ic?.CallerNumber;
                                prefil.firm = ic?.Model.StringToIntThousands("mod").ToString();
                            }
                            else {
                                prefil.firm = oc?.Model.StringToIntThousands("mod").ToString();
                            }
                            prefil.contactData = IntegrationController.ConvertPhoneNumberToGSMFormat(prefil.contactData, prefil.firm);
                            Dbg.WriteLine($"Editor {Model.Common.Editor_Model.CallEditor} prefilling number {prefil.contactData}", "AgentClientController.PrefillSearch");
                            break;
                        case Model.Common.Editor_Model.MessageEditor:
                            var msg = ctx.Messages.Where(y => y.DeviceRank >= 0 && y.MessageId == recordid).Select(y => new { y.RemoteAddress, y.Contact.ContactModel.Model }).FirstOrDefault();
                            prefil.contactData = msg?.RemoteAddress;
                            prefil.firm = msg?.Model.StringToIntThousands("mod").ToString();
                            Dbg.WriteLine($"Editor {Model.Common.Editor_Model.MessageEditor} prefilling number {prefil.contactData}", "AgentClientController.PrefillSearch");
                            break;
                        case Model.Common.Editor_Model.ChatEditor:
                            var chat = ctx.Chats.Where(y => y.ChatId == recordid).Select(y => new { y.RemotePartyName, y.Contact.ContactModel.Model }).FirstOrDefault();
                            prefil.contactData = chat?.RemotePartyName;
                            prefil.firm = chat?.Model.StringToIntThousands("mod").ToString();
                            Dbg.WriteLine($"Editor {Model.Common.Editor_Model.ChatEditor} prefilling number {prefil.contactData}", "AgentClientController.PrefillSearch");
                            break;
                        default:
                            Dbg.WriteLine($"Editor {editormodel} not found in switch block - preffiled text set to empty", "AgentClientController.PrefillSearch");
                            prefil = null;
                            break;
                    }
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Error encoutered - editor {editormodel} and exception {e}", "AgentClientController.PrefillSearch");
                }
            }
            if(prefil != null && prefil?.contactData == null) prefil.contactData = ""; //Prevents returning null to frontend, which may cause problems in later processing
            if(prefil != null && prefil?.firm == "0") {
                prefil.firm = "2";
                Dbg.WriteLine($"Prefil Firm was 0, returning default = 2", "AgentClientController.PrefillSearch");
            }
            Dbg.WriteLine($"Returning prefill - contactData {prefil.contactData} and firm {prefil.firm}", "AgentClientController.PrefillSearch");
            return prefil;
        }

        //http://localhost:60600/AgentClient/pages/MessageEditor.html?id=cf7c98dd-4ec1-48f4-92a5-75bc38482a1c
        //Assign the selected client entry to the AgentClient DB entry
        [HttpPost, Route("selectedbinding/{editormodel:maxlength(24)}/{recordid:guid}")]
        public Guid SelectedBinding([FromUri] string editormodel, [FromUri] Guid recordid, [FromBody] CtcClientInfoPoco result) {
            Dbg.WriteLine($"Starting SelectedBinding", "AgentClientController.SelectedBinding");
            Dbg.WriteLine($"Received data: brand {result.brand.name}; name {result.name}; extKey {result.id}; type id {result.type.id}", "AgentClientController.SelectedBinding");
            try {
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.SelectedBinding");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access"
                        });
                    }
                }

                Guid? contactId = null;
                Contact contact = null;
                Guid contactModelId = new Guid();
                var model = result.type.id * 1000 + result.brand.id % 1000;
                Dbg.WriteLine($"Model = {model}", "AgentClientController.SelectedBinding");
                using(var ctx = new DbDataContext()) {
                    try {
                        contactModelId = ctx.ContactModels.FirstOrDefault(z => z.Model.Equals(model.ToString())).ContactModelId;
                        Dbg.WriteLine($"ContactModelId = {contactModelId}", "AgentClientController.SelectedBinding");
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - ContactModel {model} not found: {e}", "AgentClientController.SelectedBinding");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"ContactModel {model} not found"
                        });
                    }
                    contact = ctx.Contacts.FirstOrDefault(x => x.ExternalKey.Equals(result.id) && x.ContactModelId.Equals(contactModelId));

                    if(contact == null) {
                        Dbg.WriteLine($"Contact was not found, returned null - Creating new contact", "AgentClientController.SelectedBinding");
                        contact = new Contact {
                            TimeUtc = DateTime.UtcNow,
                            FirstName = result.firstName,
                            LastName = result.name,
                            ExternalKey = result.id.ToString(),
                            ContactModelId = contactModelId,
                            Deleted = false,
                            Synchronized = false,
                            Changed = DateTime.Now
                        };
                        ctx.Contacts.InsertOnSubmit(contact);
                        ctx.SubmitChanges();
                        contactId = contact.ContactId;
                        Dbg.WriteLine($"Received data: brand {contact.LastName}; extKey {contact.ExternalKey}; model id {contact.ContactModelId}", "AgentClientController.SelectedBinding");
                        Dbg.WriteLine($"ContactId {contactId}", "AgentClientController.SelectedBinding");

                    }
                    else {
                        Dbg.WriteLine($"Contact was found - Updating contact {contact.ContactId}", "AgentClientController.SelectedBinding");
                        contactId = contact.ContactId;
                        var contactChanged = false;
                        if(!string.Equals(contact.FirstName, result.firstName)) { contact.FirstName = result.firstName; contactChanged = true; }
                        if(!string.Equals(contact.LastName, result.name)) { contact.LastName = result.name; contactChanged = true; }
                        if(contactChanged) {
                            contact.Changed = DateTime.Now;
                        }
                        ctx.SubmitChanges();
                    }
                }
                return contactId.Value;
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.SelectedBinding");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = e.Response.StatusCode,
                    ReasonPhrase = e.Response.ReasonPhrase
                });
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.SelectedBinding");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ReasonPhrase = $"Exception thrown: {e}"
                });
            }
        }

        //SelectedBinding for Portlet
        [HttpPost, Route("selectedbinding")]
        public Guid SelectedBinding([FromBody] CtcClientInfoPoco result) {
            Dbg.WriteLine($"Starting SelectedBinding", "AgentClientController.SelectedBinding");
            try {
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent {RequestContext.Principal.Identity.Name} found: {e}", "AgentClientController.SelectedBinding");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access of agent {RequestContext.Principal.Identity.Name}"
                        });
                    }
                }

                Guid? contactId = null;
                Contact contact = null;
                Guid contactModelId = new Guid();
                var model = result.type.id * 1000 + result.brand.id % 1000;
                using(var ctx = new DbDataContext()) {
                    try {
                        contactModelId = ctx.ContactModels.FirstOrDefault(z => z.Model.Equals(model.ToString())).ContactModelId;
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - ContactModel {model} not found: {e}", "AgentClientController.SelectedBinding");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"ContactModel {model} not found"
                        });
                    }
                    contact = ctx.Contacts.FirstOrDefault(x => x.ExternalKey.Equals(result.id) && x.ContactModelId.Equals(contactModelId));

                    if(contact == null) {
                        Dbg.WriteLine($"Contact was not found, returned null - Creating new contact", "AgentClientController.SelectedBinding");
                        contact = new Contact {
                            TimeUtc = DateTime.UtcNow,
                            FirstName = result.firstName,
                            LastName = result.name,
                            ExternalKey = result.id.ToString(),
                            ContactModelId = contactModelId,
                            Deleted = false,
                            Synchronized = false,
                            Changed = DateTime.Now
                        };
                        ctx.Contacts.InsertOnSubmit(contact);
                        ctx.SubmitChanges();
                        contactId = contact.ContactId;
                    }
                    else {
                        Dbg.WriteLine($"Contact was found - Updating contact", "AgentClientController.SelectedBinding");
                        contactId = contact.ContactId;
                        var contactChanged = false;
                        if(!string.Equals(contact.FirstName, result.firstName)) { contact.FirstName = result.firstName; contactChanged = true; }
                        if(!string.Equals(contact.LastName, result.name)) { contact.LastName = result.name; contactChanged = true; }
                        if(contactChanged) {
                            contact.Changed = DateTime.Now;
                        }
                        ctx.SubmitChanges();
                    }
                }
                return contactId.Value;
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.SelectedBinding");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = e.Response.StatusCode,
                    ReasonPhrase = e.Response.ReasonPhrase
                });
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.SelectedBinding");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ReasonPhrase = $"Exception thrown: {e}"
                });
            }
        }

        // http://localhost:60600/RandomClient/CtcDetail/75B2084F-6C1B-4681-9CEF-7925053546B4
        // Get client-client details
        [HttpGet, Route("ctcdetail/{contactId:guid}")]
        public CtcClientDetailPoco CtcDetail([FromUri] Guid contactId) {
            Dbg.WriteLine($"Starting CtcDetail", "AgentClientController.CtcDetail");
            try {
                RO.IAgentStatic agent = null;
                string agentName = "";
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        agent = AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                        agentName = agent.SystemPin;
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.CtcDetail");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access of agent {RequestContext.Principal.Identity.Name} ctx: {ctx.Connection?.ConnectionString} e.Message: {e.Message} e.InnerEx.Message: {e.InnerException?.Message}"
                        });
                    }
                }

                var key = contactId.ToString();
                var cached = CtcClientDetailsCache.TryGet(key);
                if(cached != null) {
                    return cached.Item;
                }
                else {
                    //// DEBUGGING PURPOSES
                    
                    if(propsAttr.UseFakeCallerNumber) return new CtcClientDetailPoco().GetDebuggingData(887365, propsAttr.FakeCallerNumber, "admin");

                    //// Production
                    Task<CtcClientDetailPoco> clientDetail;
                    var sessionStart = DateTime.Now;
                    var currentSession = getDetailSessions.GetOrAdd(contactId, sessionStart);
                    if(sessionStart == currentSession || sessionStart > currentSession.AddSeconds(10)) {
                        clientDetail = Task.Run(() => GetDetail(contactId, agentName));
                        if(clientDetail.Result.contactType != 2 && !clientDetail.Result.duplicit) {
                            clientDetail = DetailPocoDateTimesToString(clientDetail);
                        }
                        CtcClientDetailsCache.Set(key, clientDetail.Result);
                        return clientDetail.Result;
                    }
                    else {
                        return null;
                    }
                }
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown {e}", "AgentClientController.Search");
                var clientDetail = new CtcClientDetailPoco() {
                    httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = e.Response.ReasonPhrase + " - Exception: " + e
                    }
                };
                return clientDetail;
            }
            catch(Exception e) {
                var clientDetail = new CtcClientDetailPoco() {
                    httpResponse = new HttpResponsePoco() {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = "Exception: " + e.ToString()
                    }
                };
                return clientDetail;
            }
        }

        // http://localhost:60600/RandomClient/CtcDetail/75B2084F-6C1B-4681-9CEF-7925053546B4
        //Force refresh of the client-client details
        [HttpGet, Route("forcectcdetail/{contactId:guid}")]
        public CtcClientDetailPoco ForceCtcDetail([FromUri] Guid contactId) {
            Dbg.WriteLine($"Starting ForceCtcDetail", "AgentClientController.ForceCtcDetail");
            try {
                RO.IAgentStatic agent = null;
                string agentName = "";
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        agent = AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                        agentName = agent.SystemPin;
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.ForceCtcDetail");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access"
                        });
                    }
                }

                Task<CtcClientDetailPoco> clientDetail;
                var key = contactId.ToString();
                CtcClientDetailsCache.Set(key, null);
                var sessionStart = DateTime.Now;
                var currentSession = getDetailSessions.GetOrAdd(contactId, sessionStart);
                if(sessionStart == currentSession || sessionStart > currentSession.AddSeconds(10)) {
                    clientDetail = Task.Run(() => GetDetail(contactId, agentName));
                    if(clientDetail.Result.contactType != 2 && !clientDetail.Result.duplicit) {
                        clientDetail = DetailPocoDateTimesToString(clientDetail);
                    }
                    CtcClientDetailsCache.Set(key, clientDetail.Result);
                    return clientDetail.Result;
                }
                else {
                    return null;
                }
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown {e}", "AgentClientController.Search");
                var clientDetail = new CtcClientDetailPoco() {
                    httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = e.Response.ReasonPhrase + " - Exception: " + e
                    }
                };
                return clientDetail;
            }
            catch(Exception e) {
                var clientDetail = new CtcClientDetailPoco() {
                    httpResponse = new HttpResponsePoco() {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = "Exception: " + e.ToString()
                    }
                };
                return clientDetail;
            }
        }

        // http://localhost:60600/RandomClient/CtcDetail/793722
        //Get personal notes on the client entry
        [HttpGet, Route("ctcnotes/{contactId:guid}")]
        public GetNotesAndCategoriesPoco CtcNotes([FromUri] Guid contactId) {
            Dbg.WriteLine($"Starting CtcNotes", "AgentClientController.CtcNotes");
            try {
                RO.IAgentStatic agent = null;
                using(var ctx = new DbReadOnlyContext()) {
                    try {
                        agent = AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                    }
                    catch(Exception e) {
                        Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.CtcNotes");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.Unauthorized,
                            ReasonPhrase = $"Unauthorized access"
                        });
                    }
                }

                return GetNotesAndCategories(contactId, agent);
            }
            catch(HttpResponseException e) {
                Dbg.WriteLine($"Exception thrown {e}", "AgentClientController.Search");
                GetNotesAndCategoriesPoco notesAndCats = new GetNotesAndCategoriesPoco() {
                    httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = e.Response.ReasonPhrase + " - Exception: " + e
                    }
                };
                return notesAndCats;
            }
            catch(Exception e) {
                GetNotesAndCategoriesPoco notesAndCats = new GetNotesAndCategoriesPoco() {
                    httpResponse = new HttpResponsePoco() {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = "Exception: " + e.ToString()
                    }
                };
                return notesAndCats;
            }

        }

        //Set up external call
        [HttpPost, Route("calloutboundlistcall/{contactId:guid}/")]
        public string CallOutboundListCall([FromUri] Guid contactId) {
            Dbg.WriteLine($"Starting CallOutboundListCall, contactId {contactId}", "AgentClientController.CallOutboundListCall");
            RO.IAgentStatic agent = null;
            using(var ctx = new DbReadOnlyContext()) {
                try {
                    agent = AgentHelpers.FindAgent(ctx, RequestContext.Principal);
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown - error finding an agent/no agent found: {e}", "AgentClientController.CallOutboundListCall");
                    throw new HttpResponseException(new HttpResponseMessage() {
                        StatusCode = HttpStatusCode.Unauthorized,
                        ReasonPhrase = $"Unauthorized access"
                    });
                }
            }
            var qs = Request.GetQueryNameValuePairs();
            string phoneNr = (from pr in qs where pr.Key == "phoneNr" select pr.Value).FirstOrDefault();
            if(string.IsNullOrEmpty(phoneNr)) {
                Dbg.WriteLine($"Phone number is null or empty, cannot progress, returning null", "AgentClientController.CallOutboundListCall");
                return null;
            }
            Dbg.WriteLine($"CallOutboundListCall from querystring, phoneNr {phoneNr}", "AgentClientController.CallOutboundListCall");
            using(var ctx = new DbDataContext()) {
                try {
                    var ctcModel = ctx.Contacts.Where(x => x.ContactId == contactId).Select(y => new { y.ContactModel }).FirstOrDefault();
                    if(ctcModel == null || ctcModel?.ContactModel == null) {
                        Dbg.WriteLine($"Could not find ContactModel for contact", "AgentClientController.CallOutboundListCall");
                        throw new Exception($"Could not find ContactModel for contact");
                    }
                    Dbg.WriteLine($"ctcModel found {ctcModel?.ContactModel?.DisplayName} ; {ctcModel?.ContactModel?.Model}", "AgentClientController.CallOutboundListCall");
                    Guid? outboundListId = null;
                    switch(ctcModel.ContactModel.Model) {
                        case "1002":
                            outboundListId = propsAttr.QuickCallChancePerson;
                            break;
                        case "2002":
                            outboundListId = propsAttr.QuickCallChanceBrand;
                            break;
                        case "1004":
                            outboundListId = propsAttr.QuickCallRandomClientCZPerson;
                            break;
                        case "2004":
                            outboundListId = propsAttr.QuickCallRandomClientCZBrand;
                            break;
                        case "1005":
                            outboundListId = propsAttr.QuickCallCrownPerson;
                            break;
                        case "2005":
                            outboundListId = propsAttr.QuickCallCrownBrand;
                            break;
                        case "1020":
                            outboundListId = propsAttr.QuickCallRandomClientSKPerson;
                            break;
                        case "2020":
                            outboundListId = propsAttr.QuickCallRandomClientSKBrand;
                            break;
                        default:
                            outboundListId = propsAttr.QuickCallDefault;
                            break;
                    }
                    Dbg.WriteLine($"outboundListId {outboundListId}", "AgentClientController.CallOutboundListCall");
                    var np = ConfigHelpers.Numbering;
                    var n = phoneNr.Trim();
                    if(n.StartsWith("+", StringComparison.InvariantCultureIgnoreCase) && n.Length > 1) n = np.InternationalPrefix + n.Substring(1);
                    string phoneNumber = np.GetNumberFromText(n).Limit(32);
                    Dbg.WriteLine($"Phone number for the new OC {phoneNumber}", "AgentClientController.GetOutCallParams");
                    var ocGuid = CallHelpers.CreateParamCallOut(ctx, agent, null, null, outboundListId, contactId, null, phoneNumber, true, true, null, null);
                    Dbg.WriteLine($"CreateParamCallOut returned OC {ocGuid}", "AgentClientController.CallOutboundListCall");
                    if(ocGuid != null) return ocGuid.ToString();
                    else return null;
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Error encoutered - contactId {contactId} and exception {e}", "AgentClientController.CallOutboundListCall");
                    return null;
                }
            }
        }


        static ConcurrentDictionary<Guid, SessionTimes> relatedCommunicationSessions = new ConcurrentDictionary<Guid, SessionTimes>();
        class SessionTimes
        {
            public DateTime? SessionStartTime { get; set; }
            public DateTime? SessionEndTime { get; set; }
            public HttpResponsePoco httpResponse { get; set; }

            public SessionTimes() {
                httpResponse = new HttpResponsePoco();
            }
        }

        const int ContactEventType = 241;
        private CtcRelatedPoco UpdateHistory(Guid contactId) {
            Dbg.WriteLine($"Starting UpdateHistory", "AgentClientController.UpdateHistory");

            var relatedPoco = new CtcRelatedPoco();

            if(relatedCommunicationSessions.Count >= 10000) {
                Dbg.WriteLine($"Concurrent dictionary cleanup start - entries: {relatedCommunicationSessions.Count}", "AgentClientController.UpdateHistory");
                var entriesToDelete = relatedCommunicationSessions.Where(x => x.Value.SessionEndTime < DateTime.Now.AddHours(-3) || (x.Value.SessionEndTime == null && x.Value.SessionStartTime < DateTime.Now.AddHours(-1)) || (x.Value.httpResponse.status != 0 && x.Value.httpResponse.status != Convert.ToInt32(HttpStatusCode.OK))).ToList();
                SessionTimes deletedDate = new SessionTimes();
                foreach(var entry in entriesToDelete) {
                    relatedCommunicationSessions.TryRemove(entry.Key, out deletedDate);
                }
                Dbg.WriteLine($"Concurrent dictionary cleanup finish - entries: {relatedCommunicationSessions.Count}", "AgentClientController.UpdateHistory");
            }

            SessionTimes sessionTimes = new SessionTimes();
            //Key = External Key, Value = DateTime of when the function was called
            //Returns true if added, returns false if the key already exists
            var sessionValue = relatedCommunicationSessions.GetOrAdd(contactId, sessionTimes);
            if(sessionValue.SessionEndTime != null && (DateTime.Now - sessionValue.SessionEndTime.Value).TotalMinutes <= 5) {
                Dbg.WriteLine($"Comm history is up to date", "AgentClientController.UpdateHistory");
                relatedPoco.isUpdated = true;
                return relatedPoco;
            }
            else if(sessionValue.SessionEndTime == null && sessionValue.SessionStartTime != null && (DateTime.Now - sessionValue.SessionStartTime.Value).TotalHours > 24) {
                return StartCommunicationUpdateRoutine(sessionTimes, contactId);
            }
            else if(sessionValue.SessionEndTime == null && sessionValue.SessionStartTime == null) {
                return StartCommunicationUpdateRoutine(sessionTimes, contactId);
            }
            else if(sessionValue.SessionStartTime != null && sessionValue.SessionEndTime != null && (DateTime.Now - sessionValue.SessionEndTime.Value).TotalMinutes > 5) {
                return StartCommunicationUpdateRoutine(sessionTimes, contactId);
            }
            else {
                Dbg.WriteLine($"Comm history updating", "AgentClientController.UpdateHistory");
                relatedPoco.isUpdated = false;
                return relatedPoco;
            }
        }

        private CtcRelatedPoco StartCommunicationUpdateRoutine(SessionTimes sessionTimes, Guid contactId) {
            Dbg.WriteLine($"Starting StartCommunicationUpdateRoutine", "AgentClientController.StartCommunicationUpdateRoutine");

            var relatedPoco = new CtcRelatedPoco();
            Task sessionTask;
            sessionTimes.SessionStartTime = DateTime.Now;
            sessionTimes.SessionEndTime = null;
            relatedCommunicationSessions.AddOrUpdate(contactId, sessionTimes, (x, z) => z = sessionTimes);
            sessionTask = Task.Run(() => GetCommunicationHistory(contactId));
            relatedPoco.isUpdated = false;
            return relatedPoco;
        }

        public void GetCommunicationHistory(Guid contactId) {
            Dbg.WriteLine($"Starting GetCommunicationHistory", "AgentClientController.GetCommunicationHistory");
            RO.Contact contact = null;
            RO.ContactEvent lastContactEvent;
            string model = "";
            DateTime lastUpdate = new DateTime(1990, 1, 1);
            List<GetCommunicationHistoryPoco> commHistory = new List<GetCommunicationHistoryPoco>();
            SessionTimes sessionTimes = new SessionTimes();
            relatedCommunicationSessions.TryGetValue(contactId, out sessionTimes);
            try {
                using(var ctx = new DbReadOnlyContext()) {
                    contact = ctx.Contacts.FirstOrDefault(x => x.ContactId.Equals(contactId));
                    if(contact == null) {
                        Dbg.WriteLine($"No Contact with given Contact Id {contactId} exists", "AgentClientController.GetCommunicationHistory");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"Contact not found - GetCommunicationHistory"
                        });
                    }

                    model = ctx.ContactModels.FirstOrDefault(x => x.ContactModelId.Equals(contact.ContactModelId)).Model;
                    if(string.IsNullOrEmpty(model)) {
                        Dbg.WriteLine($"Model not found", "AgentClientController.GetCommunicationHistory");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"Model not found - GetCommunicationHistory"
                        });
                    }
                    else if(!(model.Equals("1002") || model.Equals("1004") || model.Equals("1005") || model.Equals("1020")
                        || model.Equals("2002") || model.Equals("2004") || model.Equals("2005") || model.Equals("2020"))) {
                        Dbg.WriteLine($"Model not corresponding with RandClient models: {model}", "AgentClientController.GetCommunicationHistory");
                        throw new HttpResponseException(new HttpResponseMessage() {
                            StatusCode = HttpStatusCode.NotFound,
                            ReasonPhrase = $"Model not corresponding with RandClient models - GetCommunicationHistory"
                        });
                    }
                    model = model.StringToIntThousands("mod").ToString();
                    lastContactEvent = ctx.ContactEvents.Where(y => y.EventType.Equals(ContactEventType) && y.ContactId.Equals(contactId)).OrderByDescending(x => x.TimeLocal).FirstOrDefault();
                    Dbg.WriteLine($"ContactEventId: {lastContactEvent?.ContactEventId} or ContactId: {contactId}", "AgentClientController.GetCommunicationHistory");
                }
                if(lastContactEvent == null) {
                    Dbg.WriteLine($"lastContactEvent was null, creating new ContactEvent", "AgentClientController.GetCommunicationHistory");
                    //A default value in case the getHistory has never been called for the current contact before
                    lastUpdate = new DateTime(1990, 1, 1).ToLocalTime();
                    //Creates a first entry that can be later updated - also will serve as an anchor point for future calls in case it fails on the first run
                    using(var ctx = new DbDataContext()) {
                        ctx.ContactEvents.InsertOnSubmit(new ContactEvent {
                            TimeUtc = lastUpdate.ToUniversalTime(),
                            TimeLocal = lastUpdate,
                            EventType = ContactEventType,
                            ContactId = contactId
                        });
                        ctx.SubmitChanges();
                    }
                    Dbg.WriteLine($"Last ContactEvent update was on: {lastUpdate}", "AgentClientController.GetCommunicationHistory");
                }
                else {
                    lastUpdate = lastContactEvent.TimeLocal;
                    Dbg.WriteLine($"Last ContactEvent update was on: {lastUpdate}", "AgentClientController.GetCommunicationHistory");
                }
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetCommunicationHistory");
                sessionTimes.SessionStartTime = null;
                sessionTimes.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = $"Exception thrown: {e}"
                };
                return;
            }



            //DEBUGGING PURPOSES
            //commHistory.Add(new GetCommunicationHistoryPoco(1)); commHistory.Add(new GetCommunicationHistoryPoco(2)); commHistory.Add(new GetCommunicationHistoryPoco(1));
            //commHistory.Add(new GetCommunicationHistoryPoco(3)); commHistory.Add(new GetCommunicationHistoryPoco(1)); commHistory.Add(new GetCommunicationHistoryPoco(1));
            //commHistory.Add(new GetCommunicationHistoryPoco(3)); commHistory.Add(new GetCommunicationHistoryPoco(2)); commHistory.Add(new GetCommunicationHistoryPoco(1));

            Dbg.WriteLine($"Calling RandomClient getCommunicationHistory with date: {lastUpdate.ToString("s")}", "AgentClientController.GetCommunicationHistory");
            try {
                //commHistory = GetWebClient<List<GetCommunicationHistoryPoco>>("/communication/client/" + contact?.ExternalKey + "?dateFrom=" + lastUpdate.ToString("s"), model, new List<string>() {}); //"s" = ISO 8601
                commHistory = GetWebClient<List<GetCommunicationHistoryPoco>>("/communication/client/" + contact?.ExternalKey, model, new List<string>() { "dateFrom", lastUpdate.ToString("s") }); //"s" = ISO 8601
            }
            catch(HttpResponseException e) {
                sessionTimes.SessionStartTime = null;
                sessionTimes.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(e.Response.StatusCode),
                    title = e.Response.StatusCode.ToString(),
                    detail = "Could not connect to RandClient: \n - Exception: " + e.Response.ReasonPhrase
                };
                return;
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetCommunicationHistory - Could no connect to RandClient");
                sessionTimes.SessionStartTime = null;
                sessionTimes.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = $"Could not connect to RandClient: \n {e}"
                };
                return;
            }

            try {
                using(var ctx = new DbDataContext()) {
                    //Initialization doesn't have to occur on every FOR run
                    string projectCommType = "";
                    string communicationType = "";
                    Guid? gatewayId = null;
                    Guid? projectId = null;
                    string bodyTxt = "";
                    Dbg.WriteLine($"Starting communication extraction", "AgentClientController.GetCommunicationHistory");
                    for(int i = 0; i < commHistory.Count; i++) {
                        //TODO - vyresit handling null, tj nezname gateway, if(GatewayId == null)
                        try {
                            gatewayId = ctx.Gateways.FirstOrDefault(x => x.PilotAddress.Equals(commHistory[i].sender) && x.Description.Equals(commHistory[i].type))?.GatewayId;
                        }
                        catch(Exception e) {
                            Dbg.WriteLine($"Exception thrown - couldn't load gatewayId: {e} for: {commHistory[i].sender}", "AgentClientController.GetCommunicationHistory");
                        }

                        //Default comm types SMS for SMS and Email for E-mail, PM and MEMBERS
                        communicationType = (commHistory[i].type.Equals("SMS")) ? "SMS" : "Email";

                        switch(commHistory[i].type) {
                            case "E-MAIL":
                                projectCommType = "Emails";
                                break;
                            case "PM":
                                projectCommType = "Private Messages";
                                break;
                            case "SMS":
                                projectCommType = "SMS";
                                break;
                            case "MEMBERS":
                                projectCommType = "Members";
                                break;
                            default:
                                projectCommType = "ProjectNameNotSet";
                                break;
                        }

                        try {
                            projectId = ctx.Projects.FirstOrDefault(x => x.DisplayName.Equals(projectCommType) && x.Description.Equals("RandomClient"))?.ProjectId;
                        }
                        catch(Exception e) {
                            Dbg.WriteLine($"Exception thrown - couldn't load projectId: {e} of PROJECT TYPE: {projectCommType}", "AgentClientController.GetCommunicationHistory");
                        }

                        //Strips the message content of HTML tags
                        bodyTxt = commHistory[i].text.StripHtmlAndDecode();
                        var msgPh = DetermineMessagePhase(commHistory[i].status);
                        if(Dbg.Detail) Dbg.WriteLine($"MessageHistory.status: {commHistory[i].status} ; MessagePhase: {msgPh}", "AgentClientController.GetCommunicationHistory");

                        ctx.Messages.InsertOnSubmit(new Message {
                            MessageKey = commHistory[i].id.ToString(),
                            MessageType = communicationType,
                            ProjectId = projectId,
                            FromField = commHistory[i].sender,
                            GatewayId = gatewayId,
                            ReceivedSentTime = commHistory[i].sendTime,
                            EndTime = commHistory[i].sendTime,
                            SubjectField = commHistory[i].subject.Limit(320),
                            BodyHtml = commHistory[i].text,
                            BodyText = bodyTxt,
                            MessagePhase = msgPh,
                            ContactId = contactId,
                            RemoteAddress = commHistory[i].identifier,
                            ToField = commHistory[i].identifier,
                            MessageResult = Message_MessageResult.Closed,
                            TimeUtc = DateTime.Now,
                            Priority = 0,
                            Direction = Message_Direction.Out,
                            Mark = 0,
                            ConditionLevel = 0
                        });

                        //In order to avoid overloading the connection transaction limit
                        if(i % 100 == 0) {
                            var contEvent = ctx.ContactEvents.FirstOrDefault(x => x.ContactId.Equals(contactId) && x.TimeLocal.Equals(lastUpdate) && x.EventType == ContactEventType);
                            contEvent.TimeLocal = commHistory[i].sendTime;
                            contEvent.TimeUtc = DateTime.Now.ToUniversalTime();
                            ctx.SubmitChanges();
                            Dbg.WriteLine($"100 communication entries saved to DB and LastUpdate time updated: {commHistory[i].sendTime}", "AgentClientController.GetCommunicationHistory");
                        }
                    }
                    //Creates a CallEvent with the 421 group and the session start time
                    ctx.ContactEvents.InsertOnSubmit(new ContactEvent {
                        TimeUtc = sessionTimes.SessionStartTime.Value.ToUniversalTime(),
                        TimeLocal = sessionTimes.SessionStartTime.Value,
                        EventType = ContactEventType,
                        ContactId = contactId
                    });
                    ctx.SubmitChanges();
                }

                sessionTimes.SessionEndTime = DateTime.Now.ToLocalTime();
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetCommunicationHistory");
                sessionTimes.SessionStartTime = null;
                sessionTimes.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = $"Exception thrown: {e}"
                };
            }
        }

        public string DetermineMessagePhase(string status) {
            switch(status) {
                case RelatedMsgStatus_Sent:
                    return Message_MessagePhase.Sent;
                case RelatedMsgStatus_Failed:
                    return Message_MessagePhase.Failed;
                case RelatedMsgStatus_Confirmed:
                    return Message_MessagePhase.Confirmed;
                default:
                    return Message_MessagePhase.Closed;
            }
        }

        public GetNotesAndCategoriesPoco GetNotesAndCategories(Guid contactId, RO.IAgentStatic agent = null, CtcClientGetNotesPoco.Note noteToAdd = null) {
            Dbg.WriteLine($"Starting GetNotesAndCategories", "AgentClientController.GetNotesAndCategories");
            RO.Contact contact = null;

            GetNotesAndCategoriesPoco clientNotesAndCategories = new GetNotesAndCategoriesPoco() { NotesCategories = new List<ClientNotesCategoryPoco>() };
            CtcClientGetNotesPoco clientNotes = new CtcClientGetNotesPoco();
            clientNotesAndCategories.GetNotesPoco = clientNotes;
            //DEBUGGING PURPOSES
            //ClientNotesCategoryPoco noteCat1 = new ClientNotesCategoryPoco() { id = 1, name = "Účty" };
            //ClientNotesCategoryPoco noteCat2 = new ClientNotesCategoryPoco() { id = 2, name = "Bany" };
            //ClientNotesCategoryPoco noteCat3 = new ClientNotesCategoryPoco() { id = 3, name = "Platby" };

            string model = "";
            using(var ctx = new DbReadOnlyContext()) {
                contact = ctx.Contacts.FirstOrDefault(x => x.ContactId.Equals(contactId));
                if(contact == null) {
                    Dbg.WriteLine($"No Contact with given Contact Id {contactId} exists", "AgentClientController.GetNotesAndCategories");
                    clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.NotFound),
                        title = HttpStatusCode.NotFound.ToString(),
                        detail = $"Could not find contact by given ContactId: {contactId}"
                    };
                    return clientNotesAndCategories;
                }
                model = ctx.ContactModels.FirstOrDefault(x => x.ContactModelId.Equals(contact.ContactModelId))?.Model;
            }

            if(string.IsNullOrEmpty(model)) {
                Dbg.WriteLine($"ContactModelId does not correspond with any ContactModel", "AgentClientController.GetNotesAndCategories");
                clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = $"Model for given contact id {contactId} was not found"
                };
                return clientNotesAndCategories;
            }
            else if(model.Equals("2002") || model.Equals("2004") || model.Equals("2005") || model.Equals("2020")) {
                clientNotesAndCategories.contactType = 2;
                return clientNotesAndCategories;
            }
            else if(!(model.Equals("1002") || model.Equals("1004") || model.Equals("1005") || model.Equals("1020"))) {
                Dbg.WriteLine($"ContactModelId does not correspond with RandomClient model", "AgentClientController.GetNotesAndCategories");
                clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                    status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                    title = HttpStatusCode.InternalServerError.ToString(),
                    detail = $"Could not find a corresponding contact model to {model}"
                };
                return clientNotesAndCategories;
            }

            var key = model; //Leads to having 4 different keys for 4 models
            var cached = ClientNotesCategoryCache.TryGet(key);
            if(cached != null) {
                clientNotesAndCategories.NotesCategories = cached.Item;
            }
            else {
                clientNotesAndCategories.GetNotesPoco = clientNotes;
                try {
                    //notesCategories = cc_clientNotesCategory();
                    Dbg.WriteLine($"Calling RandClient - getNotesCategory", "AgentClientController.GetNotesAndCategories");
                    clientNotesAndCategories.NotesCategories = GetWebClient<List<ClientNotesCategoryPoco>>("/notes/categories", model.StringToIntThousands("mod").ToString());
                    //DEBUGGING PURPOSES
                    //clientNotesAndCategories.NotesCategories.Add(noteCat1);
                    //clientNotesAndCategories.NotesCategories.Add(noteCat2);
                    //clientNotesAndCategories.NotesCategories.Add(noteCat3);
                }
                catch(HttpResponseException e) {
                    clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = "Exception thrown - couldn't load Notes categories: " + e.Response.ReasonPhrase
                    };
                    return clientNotesAndCategories;
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown - couldn't load Notes categories: {e}", "AgentClientController.GetNotesAndCategories");
                    clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = $"Exception thrown - couldn't load Notes categories {e}"
                    };
                    return clientNotesAndCategories;
                }
                Dbg.WriteLine($"Caching note categories", "AgentClientController.GetNotesAndCategories");
                ClientNotesCategoryCache.Set(key, clientNotesAndCategories.NotesCategories);
            }

            using(var ctx = new DbReadOnlyContext()) {
                try {
                    Dbg.WriteLine($"Calling RandClient - getClientNotes", "AgentClientController.GetNotesAndCategories");
                    //cc_GetClientNotes(clientId, agentName) 
                    //clientNotes = new CtcClientGetNotesPoco(Convert.ToInt32(contact.ExternalKey), agent.SystemPin);
                    clientNotes = GetWebClient<CtcClientGetNotesPoco>("/notes/client/" + contact.ExternalKey, model, new List<string>() { "username", agent.SystemPin });
                    foreach(var note in clientNotes.notes) {
                        note.dateInsertedString = note.dateInserted.ToString("dd. MM. yyyy HH:mm");
                        note.CreatorInits = note.username.Substring(0, 3).ToUpper();
                        note.category = clientNotesAndCategories.NotesCategories.FirstOrDefault(x => x.id == note.categoryId).name;
                    }
                    clientNotesAndCategories.GetNotesPoco = clientNotes;
                }
                catch(HttpResponseException e) {
                    clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = "Could not connect to cc_getClientNotes RandClient service - Exception: " + e.Response.ReasonPhrase
                    };
                    return clientNotesAndCategories;
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown - couldn't retrieve Notes from RandClient: {e}", "AgentClientController.GetNotesAndCategories");
                    clientNotesAndCategories.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.ServiceUnavailable),
                        title = HttpStatusCode.ServiceUnavailable.ToString(),
                        detail = "Could not connect to cc_getClientNotes RandClient service"
                    };
                    return clientNotesAndCategories;
                }
            }
            if(clientNotes == null || clientNotes.noteCount == 0) {
                Dbg.WriteLine($"There are no notes available for this contact", "AgentClientController.GetNotesAndCategories");
                clientNotesAndCategories.GetNotesPoco = clientNotes;
                return clientNotesAndCategories;
            }

            //DEBUGGING PURPOSES - can be deleted along with the noteToAdd param when launched (TO DELETE)
            if(noteToAdd != null) {
                clientNotesAndCategories.GetNotesPoco.notes.Add(noteToAdd);
                clientNotesAndCategories.GetNotesPoco.noteCount++;
            }

            return clientNotesAndCategories;
        }

        static ConcurrentDictionary<Guid, DateTime> getDetailSessions = new ConcurrentDictionary<Guid, DateTime>();

        public CtcClientDetailPoco GetDetail(Guid contactId, string agentName) {
            Dbg.WriteLine($"Starting GetDetail", "AgentClientController.GetDetail");
            if(getDetailSessions.Count >= 10000) {
                Dbg.WriteLine($"Concurrent dictionary cleanup start - entries: {getDetailSessions.Count}", "AgentClientController.GetDetail");
                var stuff = getDetailSessions.Where(x => x.Value < DateTime.Now.AddHours(-3)).ToList();
                DateTime deletedDate;
                foreach(var st in stuff) {
                    getDetailSessions.TryRemove(st.Key, out deletedDate);
                }
                Dbg.WriteLine($"Concurrent dictionary cleanup finish - entries: {getDetailSessions.Count}", "AgentClientController.GetDetail");
            }

            CtcClientDetailPoco clientDetail = new CtcClientDetailPoco();
            string model = "";

            using(var ctx = new DbDataContext()) {

                var contact = ctx.Contacts.FirstOrDefault(x => x.ContactId.Equals(contactId));
                if(contact == null) {
                    Dbg.WriteLine($"No Contact with given contactId exists", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = $"Could not find contact with given ContactId: {contactId}"
                    };
                    return clientDetail;
                }
                //Dummy contacts for duplicity will have empty ExternalKey which might result in an error later on
                if(string.IsNullOrEmpty(contact.ExternalKey)) {
                    Dbg.WriteLine($"No ExternalKey in dupicity dummy contacts", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.OK),
                        title = HttpStatusCode.OK.ToString(),
                        detail = $"No ExternalKey in dupicity dummy contacts: {contactId}"
                    };
                    clientDetail.duplicit = true;
                    clientDetail.contactType = 0;
                    return clientDetail;
                }
                clientDetail.duplicit = false;
                model = ctx.ContactModels.FirstOrDefault(x => x.ContactModelId.Equals(contact.ContactModelId))?.Model;
                if(model == null) {
                    Dbg.WriteLine($"ContactModelId does not correspond with any ContactModel", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = $"Model for given ContactId {contactId} was not found"
                    };
                    return clientDetail;
                }
                else if(!(model.Equals("1002") || model.Equals("1004") || model.Equals("1005") || model.Equals("1020")
                    || model.Equals("2002") || model.Equals("2004") || model.Equals("2005") || model.Equals("2020"))) {
                    Dbg.WriteLine($"ContactModelId does not correspond with RandomClient model", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = $"Could not find a corresponding contact model to {model}"
                    };
                    return clientDetail;
                }


                CtcClientInfoPoco clientInfoPoco = null;
                CtcClientInfosPoco clientInfosPoco;
                bool branchFound = false;
                try {
                    // TODO - Re-comment for deployment
                    //clientInfoPoco = new CtcClientInfosPoco(searchType: "basic", clientId: Convert.ToInt32(contact.ExternalKey), firm: StringToIntThousands(model, "mod")).contacts[0];
                    Dbg.WriteLine($"Calling getClientInfo: clientId = {contact.ExternalKey}; type BASIC", "AgentClientController.GetDetail");
                    //StartsWith 10 = klient, 20 = pobocka
                    if(model.StartsWith("10")) {
                        clientInfosPoco = GetWebClient<CtcClientInfosPoco>("/search", model, new List<string>() { "type", "BASIC", "clientId", contact.ExternalKey });
                        if(clientInfosPoco.contactCount != 0) clientInfoPoco = clientInfosPoco.contacts[0];
                        contact.FirstName = clientInfoPoco.firstName;
                        contact.LastName = clientInfoPoco.name;
                        ctx.SubmitChanges();
                    }
                    //search currently not searching for branches when using clientId - workaround
                    else {
                        var phoneNr = ctx.InboundCalls.Where(x => x.ContactId == contactId).OrderByDescending(y => y.TimeUtc).Select(z => z.CallerNumber).ToList()[0];
                        if(model.EndsWith("20")) { //SK
                            phoneNr = "%2B421" + phoneNr.Substring(phoneNr.Length - 9);
                        }
                        else { //CZ
                            phoneNr = "%2B420" + phoneNr.Substring(phoneNr.Length - 9);
                        }
                        Dbg.WriteLine($"Trying to find a branch - workaround block - with Phone Number: {phoneNr}", "AgentClientController.GetDetail");
                        clientInfosPoco = GetWebClient<CtcClientInfosPoco>("/search", model, new List<string>() { "type", "BASIC", "phoneNumber", phoneNr });
                        if(clientInfosPoco.contactCount != 0) {
                            clientInfoPoco = clientInfosPoco.contacts[0];
                            clientDetail.id = clientInfoPoco.branchCode ?? -1;
                            branchFound = true;
                        }
                    }
                }
                catch(HttpResponseException e) {
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = "Could not connect to cc_getClientInfo RandClient service - Exception: " + e.Response.ReasonPhrase
                    };
                    return clientDetail;
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.ServiceUnavailable),
                        title = HttpStatusCode.ServiceUnavailable.ToString(),
                        detail = "Could not connect to cc_getClientInfo RandClient service",
                    };
                    return clientDetail;
                }
                Dbg.WriteLine($"Branch found: {branchFound}", "AgentClientController.GetDetail");
                if(branchFound) {
                    Dbg.WriteLine($"No client info returned from the RandClient server - client not found - assuming branch", "AgentClientController.GetDetail");
                    clientDetail.contactType = 2;
                    return clientDetail;
                }
                if(clientInfoPoco == null) {
                    Dbg.WriteLine($"Client not found - /Search did not return any contact - clientId {contact.ExternalKey}, model {model}", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.NotFound),
                        title = HttpStatusCode.NotFound.ToString(),
                        detail = $"Klient nenalezen - clientId {contact.ExternalKey}, model {model}",
                    };
                    return clientDetail;
                }

                try {
                    // TODO - Re-comment for deployment
                    //clientDetail = new CtcClientDetailPoco(Convert.ToInt32(contact.ExternalKey), "Admin");
                    Dbg.WriteLine($"Calling getClientInfo: clientId = {contact.ExternalKey}; Model = {model}", "AgentClientController.GetDetail");
                    clientDetail = GetWebClient<CtcClientDetailPoco>("/client/" + contact.ExternalKey, model, new List<string>() { "username", agentName });
                    //clientDetail = GetWebClient<CtcClientDetailPoco>("/client/" + contact.ExternalKey + "?username=" + agentName.ToLower(), model, new List<string>() {});
                }
                catch(HttpResponseException e) {
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(e.Response.StatusCode),
                        title = e.Response.StatusCode.ToString(),
                        detail = "Could not connect to cc_getClientDetail RandClient service - Exception: " + e.Response.ReasonPhrase
                    };
                    return clientDetail;
                }
                catch(Exception e) {
                    Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetDetail");
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.ServiceUnavailable),
                        title = HttpStatusCode.ServiceUnavailable.ToString(),
                        detail = $"{agentName} Could not connect to cc_getClientDetail RandClient service: {e}"
                    };
                    return clientDetail;
                }
                if(clientDetail == null) {
                    Dbg.WriteLine($"No object details returned from the RandClient server", "AgentClientController.GetDetail");
                    //In this case, a client has to be returned
                    clientDetail.httpResponse = new HttpResponsePoco {
                        status = Convert.ToInt32(HttpStatusCode.InternalServerError),
                        title = HttpStatusCode.InternalServerError.ToString(),
                        detail = "cc_getClientDetail RandClient service has not returned any entries"
                    };
                    return clientDetail;
                }
                //Contact First name and Last name update
                Dbg.WriteLine($"Updating contact name", "AgentClientController.GetDetail");
                contact.FirstName = clientInfoPoco.firstName;
                contact.LastName = clientInfoPoco.name;
                ctx.SubmitChanges();
                Dbg.WriteLine($"Returning {clientDetail}", "AgentClientController.GetDetail");
                return clientDetail;

            }
        }

        public static T GetWebClient<T>(string urlConstruct, string model, List<string> paramList = null) {
            WebClient webClient;
            string fullUrl;
            WebClientConfig(urlConstruct, model, out webClient, out fullUrl, paramList);

            string dataJsonString = "";
            try {
                webClient.Encoding = Encoding.UTF8;

                dataJsonString = webClient.DownloadString(fullUrl);
                //dataJsonString = webClient.DownloadString("http://adapter.call-center.czt1.k8s.RandomClient.it/api/v1/call-center/client/11171651/open?username=tomasvicherek");
                Dbg.WriteLine($"WebClient received data: {dataJsonString}", "AgentClientController.GetWebClient");
                var pocoData = JsonConvert.DeserializeObject<T>(dataJsonString);
                Dbg.WriteLine($"WebClient deserialization successful", "AgentClientController.GetWebClient");
                return pocoData;
            }
            catch(WebException e) {
                Dbg.WriteLine($"WebException thrown: {e} ; DataJsonString: {dataJsonString}", "AgentClientController.GetWebClient");
                var jsonParts = dataJsonString.Split(',');
                if(jsonParts.Length > 2 && jsonParts[2].ToLower().Contains("detail")) {
                    throw new HttpResponseException(new HttpResponseMessage() {
                        StatusCode = HttpStatusCode.BadRequest,
                        ReasonPhrase = "RandomClient return msg: " + jsonParts[2]
                    });
                }
                else {
                    throw new HttpResponseException(new HttpResponseMessage() {
                        StatusCode = HttpStatusCode.BadRequest,
                        ReasonPhrase = Regex.Replace(e.ToString(), @"\t|\n|\r", "")
                    });
                }
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.GetWebClient");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ReasonPhrase = $"GetWebClient failed on model {model} and URL {fullUrl}; JsonString: {dataJsonString}"
                });
            }
        }

        public static void PostWebClient(string urlConstruct, string model, List<string> paramList = null) {
            WebClient webClient;
            string fullUrl;
            WebClientConfig(urlConstruct, model, out webClient, out fullUrl, paramList);

            try {
                webClient.Encoding = Encoding.UTF8;
                webClient.UploadString(fullUrl, "");
                Dbg.WriteLine($"PostWebClient UploadString successful", "AgentClientController.PostWebClient");
            }
            catch(WebException e) {
                Dbg.WriteLine($"WebException thrown: {e} ; {e.Message}", "AgentClientController.PostWebClient");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.BadRequest,
                    ReasonPhrase = Regex.Replace(e.ToString(), @"\t|\n|\r", "")
                });
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.PostWebClient");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ReasonPhrase = $"GetWebClient failed on model {model} and URL {fullUrl}"
                });
            }
        }

        private static void WebClientConfig(string urlConstruct, string model, out WebClient webClient, out string fullUrl, List<string> paramList = null) {
            string baseUrl;
            baseUrl = GetBaseUrl(model);
            fullUrl = string.Concat(baseUrl + urlConstruct);
            webClient = new WebClient();

            try {
                //baseUrl = "http://adapter.call-center.czt1.k8s.RandomClient.it"; //Mock URL from RandomClient
                webClient.Credentials = CredentialCache.DefaultCredentials;
                webClient.Headers[HttpRequestHeader.ContentType] = "application/json";
                //Test/DEV tomasvicherek/zuh59d
                string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(propsAttr.UserName_RandClient + ":" + propsAttr.Password_RandClient));
                webClient.Headers[HttpRequestHeader.Authorization] = string.Format("Basic {0}", credentials);

                if(paramList != null) {
                    for(int i = 0; i < paramList.Count; i = i + 2) {
                        if(string.IsNullOrEmpty(paramList[i + 1]))
                            continue;
                        //RandClient can't handle the initial Date format
                        if(paramList[i].Equals("clientDateOfBirth")) {
                            var dateFromStr = DateTime.Parse(paramList[i + 1]);
                            paramList[i + 1] = dateFromStr.ToString("yyyy-MM-dd");
                        }

                        webClient.QueryString.Add(paramList[i], paramList[i + 1]);
                        Dbg.WriteLine($"Adding param: {paramList[i]} with value of {paramList[i + 1]}", "AgentClientController.WebClientConfig");
                    }
                }
                Dbg.WriteLine($"Calling: {fullUrl}", "AgentClientController.WebClientConfig");
                //DEBUGGING PURPOSES
                //foreach(var key in webClient.QueryString.AllKeys) {
                //    Dbg.WriteLine($"QueryString key: {key} - with value: {webClient.QueryString.GetValues(key)[0].ToString()}", "AgentClientController.GetWebClient");
                //}
            }
            catch(Exception e) {
                Dbg.WriteLine($"WebClientConfig failed: {Regex.Replace(e.ToString(), @"\t|\n|\r", "")}", "AgentClientController.WebClientConfig");
            }
        }

        private static string GetBaseUrl(string model) {
            Dbg.WriteLine($"Getting base URL for model: {model}", "AgentClientController.GetBaseUrl");
            model = model.StringToIntThousands("mod").ToString();
            string baseUrl;
            switch(model) {
                case "2":
                case "Chance":
                    baseUrl = Properties.Settings.Default.URLRandClientChance;
                    break;
                case "4":
                case "RandomClient CZ":
                    baseUrl = Properties.Settings.Default.URLRandClientRandomClientCZ;
                    break;
                case "5":
                case "Crown":
                    baseUrl = Properties.Settings.Default.URLRandClientCrown;
                    break;
                case "20":
                case "RandomClient SK":
                    baseUrl = Properties.Settings.Default.URLRandClientRandomClientSK;
                    break;
                default:
                    throw new HttpResponseException(new HttpResponseMessage() {
                        StatusCode = HttpStatusCode.InternalServerError,
                        ReasonPhrase = $"Model  {model}  not corresponding with any firm address (url)"
                    });
            }
            return baseUrl;
        }

        public Task<CtcClientDetailPoco> DetailPocoDateTimesToString(Task<CtcClientDetailPoco> detailPoco) {
            Dbg.WriteLine($"Starting DetailPocoDateTimesToString", "AgentClientController.DetailPocoDateTimesToString");
            try {
                if(detailPoco.Result != null) {
                    Dbg.WriteLine($"detailPoco.Result is not null - transposing DateTimes to string", "AgentClientController.DetailPocoDateTimesToString");
                    detailPoco.Result.BirthdayString = detailPoco?.Result?.dateOfBirth?.ToString("dd. MM. yyyy");
                    detailPoco.Result.RegistrationStateFromString = detailPoco?.Result?.registrationStateUpdateDate?.ToString("dd. MM. yyyy HH:mm");
                    detailPoco.Result.VIPMemberFromString = detailPoco?.Result?.vipClubFrom?.ToString("dd. MM. yyyy");
                    detailPoco.Result.VIPMemberToString = detailPoco?.Result?.vipClubTo?.ToString("dd. MM. yyyy");
                    foreach(CtcClientDetailPoco.WebtipBAN ban in detailPoco?.Result?.webtipBans) {
                        ban.BanDurationString = ban.end?.ToString("dd. MM. yyyy HH:mm");
                        if(ban.BanDurationString.Equals("01. 01. 2100") || ban.BanDurationString.Equals("1. 1. 2100")) ban.BanDurationString = "permanent";
                    }
                }
                else Dbg.WriteLine($"detailPoco.Result is null, DetailPocoDateTimesToString skipped and returning", "AgentClientController.DetailPocoDateTimesToString");
            }
            catch(Exception e) {
                Dbg.WriteLine($"Exception thrown: {e}", "AgentClientController.DetailPocoDateTimesToString");
                throw new HttpResponseException(new HttpResponseMessage() {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ReasonPhrase = $"DetailPocoDateTimesToString failed while processing: {detailPoco?.Result?.BirthdayString}; {detailPoco?.Result?.RegistrationStateFromString}; {detailPoco?.Result?.VIPMemberFromString}; {detailPoco?.Result?.VIPMemberToString}"
                });
            }
            Dbg.WriteLine($"Finishing DetailPocoDateTimesToString", "AgentClientController.DetailPocoDateTimesToString");
            return detailPoco;
        }
    }
}


namespace ExtensionMethods
{
    public static class StringExtensions
    {
        public static int StringToIntThousands(this string str, string act) {
            switch(act) {
                case "add":
                    return Convert.ToInt32(str) + 1000;
                case "mod":
                    return Convert.ToInt32(str) % 1000;
                default:
                    Dbg.WriteLine($"Forgot to specify method, returning ADD", "AgentClientController.StringToIntThousands");
                    return Convert.ToInt32(str) + 1000;
            }
        }
    }
}