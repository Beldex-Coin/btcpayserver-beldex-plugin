using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Beldex.Configuration;
using BTCPayServer.Plugins.Beldex.Payments;
using BTCPayServer.Plugins.Beldex.Services;
using BTCPayServer.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NBitcoin;

using NBXplorer;

namespace BTCPayServer.Plugins.Beldex;

public class BeldexPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        var pluginServices = (PluginServiceCollection)services;
        var prov = pluginServices.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var chainName = prov.NetworkType;

        var network = new BeldexLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "BDX",
            DisplayName = "Beldex",
            Divisibility = 5,   // Beldex has 9 decimal places but Beldex wallets generally use 5 decimal places
            DefaultRateRules = new[]
            {
                    "BDX_X   = BDX_BTC * BTC_X",
                    "BDX_BTC = hitbtc(BDX_BTC)",
                    "BTC_X   = kraken(BTC_X)"
                },
            CryptoImagePath = "beldex.svg",
            UriScheme = "beldex"
        };
        var blockExplorerLink = chainName == ChainName.Mainnet
                    ? "https://explorer.beldex.io/tx/{0}"
                    : "https://testnet.beldex.dev/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("BDX");
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));


        services.AddSingleton(provider =>
                ConfigureBeldexLikeConfiguration(provider));
        services.AddHttpClient("BDXclient")
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var configuration = provider.GetRequiredService<BeldexLikeConfiguration>();
                if (!configuration.BeldexLikeConfigurationItems.TryGetValue("BDX", out var bdxConfig) || bdxConfig.Username is null || bdxConfig.Password is null)
                {
                    return new HttpClientHandler();
                }
                return new HttpClientHandler
                {
                    Credentials = new NetworkCredential(bdxConfig.Username, bdxConfig.Password),
                    PreAuthenticate = true
                };
            });
        services.AddSingleton<BeldexRPCProvider>();
        services.AddHostedService<BeldexLikeSummaryUpdaterHostedService>();
        services.AddHostedService<BeldexListener>();
        services.AddSingleton(provider =>
                (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(BeldexLikePaymentMethodHandler), network));
        services.AddSingleton(provider =>
(IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(BeldexPaymentLinkExtension), network, pmi));
        services.AddSingleton(provider =>
(ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(BeldexCheckoutModelExtension), network, pmi));

        services.AddUIExtension("store-nav", "/Views/Beldex/StoreNavBeldexExtension.cshtml");
        services.AddUIExtension("store-wallets-nav", "/Views/Beldex/StoreWalletsNavBeldexExtension.cshtml");
        services.AddUIExtension("store-invoices-payments", "/Views/Beldex/ViewBeldexLikePaymentData.cshtml");
        services.AddSingleton<ISyncSummaryProvider, BeldexSyncSummaryProvider>();
    }
    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
            {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }

    private static BeldexLikeConfiguration ConfigureBeldexLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new BeldexLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<BeldexLikeSpecificBtcPayNetwork>();

        foreach (var beldexLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{beldexLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                    null);
            var walletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{beldexLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
            var walletDaemonWalletDirectory =
                configuration.GetOrDefault<string>(
                    $"{beldexLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
            var daemonUsername =
                configuration.GetOrDefault<string>(
                    $"{beldexLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
            var daemonPassword =
                configuration.GetOrDefault<string>(
                    $"{beldexLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
            if (daemonUri == null || walletDaemonUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<BeldexPlugin>>();
                var cryptoCode = beldexLikeSpecificBtcPayNetwork.CryptoCode.ToUpperInvariant();
                if (daemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_DAEMON_URI is not configured");
                }
                if (walletDaemonUri is null)
                {
                    logger.LogWarning($"BTCPAY_{cryptoCode}_WALLET_DAEMON_URI is not configured");
                }
                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.BeldexLikeConfigurationItems.Add(beldexLikeSpecificBtcPayNetwork.CryptoCode, new BeldexLikeConfigurationItem
                {
                    DaemonRpcUri = daemonUri,
                    Username = daemonUsername,
                    Password = daemonPassword,
                    InternalWalletRpcUri = walletDaemonUri,
                    WalletDirectory = walletDaemonWalletDirectory
                });
            }
        }
        return result;
    }
}