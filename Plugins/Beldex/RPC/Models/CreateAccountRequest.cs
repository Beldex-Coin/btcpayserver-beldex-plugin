using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("label")] public string Label { get; set; }
    }
}