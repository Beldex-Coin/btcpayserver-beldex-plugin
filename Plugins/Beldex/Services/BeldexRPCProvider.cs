using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Beldex.Configuration;
using BTCPayServer.Plugins.Beldex.RPC;
using BTCPayServer.Plugins.Beldex.RPC.Models;

using Microsoft.Extensions.Logging;

using NBitcoin;

namespace BTCPayServer.Plugins.Beldex.Services
{
    public class BeldexRPCProvider
    {
        private readonly BeldexLikeConfiguration _beldexLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, JsonRpcClient> DaemonRpcClients;
        public ImmutableDictionary<string, JsonRpcClient> WalletRpcClients;
        private readonly ILogger<BeldexRPCProvider> _logger;

        private readonly ConcurrentDictionary<string, BeldexLikeSummary> _summaries = new();

        public ConcurrentDictionary<string, BeldexLikeSummary> Summaries => _summaries;

        public BeldexRPCProvider(BeldexLikeConfiguration beldexLikeConfiguration,
            EventAggregator eventAggregator,
            ILogger<BeldexRPCProvider> logger,
            IHttpClientFactory httpClientFactory)
        {
            _beldexLikeConfiguration = beldexLikeConfiguration;
            _eventAggregator = eventAggregator;
            _logger = logger;
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
            return _summaries.ContainsKey(cryptoCode) && IsAvailable(_summaries[cryptoCode]);
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

        public void DeleteWallet()
        {
            if (!_beldexLikeConfiguration.BeldexLikeConfigurationItems.TryGetValue("BDX", out var configItem))
            {
                _logger.LogWarning("DeleteWallet: No BDX configuration found.");
                return;
            }

            if (string.IsNullOrEmpty(configItem.WalletDirectory))
            {
                _logger.LogWarning("DeleteWallet: WalletDirectory is null or empty for BDX configuration.");
                return;
            }
            try
            {
                var walletFile = Path.Combine(configItem.WalletDirectory, "view_wallet");
                var keysFile = walletFile + ".keys";
                var passwordFile = Path.Combine(configItem.WalletDirectory, "password");

                if (File.Exists(walletFile))
                {
                    File.Delete(walletFile);
                }
                if (File.Exists(keysFile))
                {
                    File.Delete(keysFile);
                }
                if (File.Exists(passwordFile))
                {
                    File.Delete(passwordFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete wallet files in directory {Dir}",
                    configItem.WalletDirectory);
            }
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

            var changed = !_summaries.ContainsKey(cryptoCode) || IsAvailable(cryptoCode) != IsAvailable(summary);

            _summaries.AddOrReplace(cryptoCode, summary);
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