using System;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Logging;
using BTCPayServer.Plugins.Beldex.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Beldex.Services
{
    public class BeldexLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly BeldexRPCProvider _BeldexRpcProvider;
        private readonly BeldexLikeConfiguration _beldexLikeConfiguration;

        public Logs Logs { get; }

        private CancellationTokenSource _Cts;
        public BeldexLikeSummaryUpdaterHostedService(BeldexRPCProvider beldexRpcProvider, BeldexLikeConfiguration beldexLikeConfiguration, Logs logs)
        {
            _BeldexRpcProvider = beldexRpcProvider;
            _beldexLikeConfiguration = beldexLikeConfiguration;
            Logs = logs;
        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var beldexLikeConfigurationItem in _beldexLikeConfiguration.BeldexLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, beldexLikeConfigurationItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Beldex-like daemons ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _BeldexRpcProvider.UpdateSummary(cryptoCode);
                        if (_BeldexRpcProvider.IsAvailable(cryptoCode))
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                        }
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in Summary updater ({cryptoCode})");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
                // ignored
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts?.Cancel();
            _Cts?.Dispose();
            return Task.CompletedTask;
        }
    }
}