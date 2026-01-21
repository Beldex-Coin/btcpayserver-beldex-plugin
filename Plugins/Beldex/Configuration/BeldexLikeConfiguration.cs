using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Beldex.Configuration
{
    public class BeldexLikeConfiguration
    {
        public Dictionary<string, BeldexLikeConfigurationItem> BeldexLikeConfigurationItems { get; set; } = [];
    }

    public class BeldexLikeConfigurationItem
    {
        public Uri DaemonRpcUri { get; set; }
        public Uri InternalWalletRpcUri { get; set; }
        public string WalletDirectory { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}