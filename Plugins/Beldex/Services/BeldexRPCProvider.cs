using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Beldex.Configuration;
using BTCPayServer.Plugins.Beldex.RPC;
using BTCPayServer.Plugins.Beldex.RPC.Models;

using NBitcoin;

namespace BTCPayServer.Plugins.Beldex.Services
{
    public class BeldexRpcProvider
    {
        private readonly BeldexLikeConfiguration _beldexLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;

        private readonly ConcurrentDictionary<string, BeldexLikeSummary> _summaries = new();

        public ConcurrentDictionary<string, BeldexLikeSummary> Summaries { get; } = new();

        public BeldexRpcProvider(BeldexLikeConfiguration beldexLikeConfiguration,
            EventAggregator eventAggregator,
            IHttpClientFactory httpClientFactory)
        {
            _beldexLikeConfiguration = beldexLikeConfiguration;
            _eventAggregator = eventAggregator;
            DaemonRpcClients =
                _beldexLikeConfiguration.BeldexLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.DaemonRpcUri, pair.Value.Username, pair.Value.Password,
                        httpClientFactory.CreateClient($"{pair.Key}client")));
            WalletRpcClients =
                _beldexLikeConfiguration.BeldexLikeConfigurationItems.ToImmutableDictionary(pair => pair.Key,
                    pair => new JsonRpcClient(pair.Value.InternalWalletRpcUri, "", "",
                        httpClientFactory.CreateClient($"{pair.Key}client")));
        }

        public bool IsConfigured(string cryptoCode) => WalletRpcClients.ContainsKey(cryptoCode) && DaemonRpcClients.ContainsKey(cryptoCode);
        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
        }

        private bool IsAvailable(BeldexLikeSummary summary)
        {
            return summary.Synced &&
                   summary.WalletAvailable;
        }

        public async Task CloseWallet(string cryptoCode)
        {
            if (!WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                throw new InvalidOperationException($"Wallet RPC client not found for {cryptoCode}");
            }

            await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, object>(
                "close_wallet", JsonRpcClient.NoRequestModel.Instance);
        }

        public string GetWalletDirectory(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return !_beldexLikeConfiguration.BeldexLikeConfigurationItems.TryGetValue(cryptoCode, out var configItem)
                ? null
                : configItem.WalletDirectory;
        }

        public async Task<BeldexLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!DaemonRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var daemonRpcClient) ||
                !WalletRpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var walletRpcClient))
            {
                return null;
            }

            var summary = new BeldexLikeSummary();
            try
            {
                var daemonResult =
                    await daemonRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetInfoResponse>("get_info",
                        JsonRpcClient.NoRequestModel.Instance);
                summary.TargetHeight = daemonResult.TargetHeight.GetValueOrDefault(0);
                summary.CurrentHeight = daemonResult.Height;
                summary.TargetHeight = summary.TargetHeight == 0 ? summary.CurrentHeight : summary.TargetHeight;
                summary.Synced = !daemonResult.BusySyncing;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }
            try
            {
                var walletResult =
                    await walletRpcClient.SendCommandAsync<JsonRpcClient.NoRequestModel, GetHeightResponse>(
                        "get_height", JsonRpcClient.NoRequestModel.Instance);
                summary.WalletHeight = walletResult.Height;
                summary.WalletAvailable = true;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            var changed = !Summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            Summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new BeldexDaemonStateChange() { Summary = summary, CryptoCode = cryptoCode });
            }

            return summary;
        }

        public class BeldexDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public BeldexLikeSummary Summary { get; set; }
        }

        public class BeldexLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}