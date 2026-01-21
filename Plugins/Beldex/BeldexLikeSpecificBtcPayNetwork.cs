namespace BTCPayServer.Plugins.Beldex;

public class BeldexLikeSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation = 10;
    public string UriScheme { get; set; }
}