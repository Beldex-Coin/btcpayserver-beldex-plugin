using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Beldex.RPC.Models;

public class GetBalanceResponse
{
    [JsonProperty("unlocked_balance")] public long UnlockedBalance { get; set; }
}