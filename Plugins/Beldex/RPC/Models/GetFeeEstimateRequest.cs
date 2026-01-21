using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models
{
    public class GetFeeEstimateRequest
    {
        [JsonProperty("grace_blocks")] public int? GraceBlocks { get; set; } = 0;
    }
}