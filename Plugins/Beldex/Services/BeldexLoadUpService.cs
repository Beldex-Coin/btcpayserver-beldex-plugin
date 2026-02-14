using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Beldex.RPC.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Beldex.Services;

public class BeldexLoadUpService : IHostedService
{
    private const string CryptoCode = "BDX";
    private readonly ILogger<BeldexLoadUpService> _logger;
    private readonly BeldexRpcProvider _beldexRpcProvider;

    public BeldexLoadUpService(ILogger<BeldexLoadUpService> logger, BeldexRpcProvider beldexRpcProvider)
    {
        _beldexRpcProvider = beldexRpcProvider;
        _logger = logger;
    }

    [Obsolete("Remove optional password parameter")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempt to load existing wallet");

            string walletDir = _beldexRpcProvider.GetWalletDirectory(CryptoCode);
            if (!string.IsNullOrEmpty(walletDir))
            {
                string password = await TryToGetPassword(walletDir, cancellationToken);

                await _beldexRpcProvider.WalletRpcClients[CryptoCode]
                    .SendCommandAsync<OpenWalletRequest, OpenWalletResponse>("open_wallet",
                        new OpenWalletRequest { Filename = "wallet", Password = password }, cancellationToken);

                await _beldexRpcProvider.UpdateSummary(CryptoCode);
                _logger.LogInformation("Existing wallet successfully loaded");
            }
            else
            {
                _logger.LogInformation("No wallet directory configured, skipping wallet migration");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load {CryptoCode} wallet. Error Message: {ErrorMessage}", CryptoCode,
                ex.Message);
        }
    }

    [Obsolete("Password is obsolete due to the inability to fully separate the password file from the wallet file.")]
    private async Task<string> TryToGetPassword(string walletDir, CancellationToken cancellationToken)
    {
        string password = "";
        string passwordFile = Path.Combine(walletDir, "password");
        if (File.Exists(passwordFile))
        {
            password = await File.ReadAllTextAsync(passwordFile, cancellationToken);
            password = password.Trim();
        }
        else
        {
            _logger.LogInformation("No password file found - ignoring");
        }

        return password;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}