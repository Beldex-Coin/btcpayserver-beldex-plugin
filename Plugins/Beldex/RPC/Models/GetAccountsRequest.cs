using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models
{
    public partial class GetAccountsRequest
    {
        [JsonProperty("tag")] public string Tag { get; set; }
    }
}