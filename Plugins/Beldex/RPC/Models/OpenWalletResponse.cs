using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models
{
    public class OpenWalletResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("jsonrpc")] public string Jsonrpc { get; set; }
        [JsonProperty("result")] public object Result { get; set; }
        [JsonProperty("error")] public ErrorResponse Error { get; set; }
    }
}