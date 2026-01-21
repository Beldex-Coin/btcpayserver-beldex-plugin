using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models
{
    public class GetFeeEstimateResponse
    {
        [JsonProperty("fee_per_byte")] public long FeePerByte { get; set; }
        [JsonProperty("fee_per_output")] public long FeePerOutput { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("untrusted")] public bool Untrusted { get; set; }
    }
}