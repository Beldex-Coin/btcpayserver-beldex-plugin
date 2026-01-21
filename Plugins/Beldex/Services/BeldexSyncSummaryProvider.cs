using System.Collections.Generic;
using System.Linq;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Beldex.Services
{
    public class BeldexSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly BeldexRPCProvider _beldexRpcProvider;

        public BeldexSyncSummaryProvider(BeldexRPCProvider beldexRpcProvider)
        {
            _beldexRpcProvider = beldexRpcProvider;
        }

        public bool AllAvailable()
        {
            return _beldexRpcProvider.Summaries.All(pair => pair.Value.DaemonAvailable);
        }

        public string Partial { get; } = "/Views/Beldex/BeldexSyncSummary.cshtml";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _beldexRpcProvider.Summaries.Select(pair => new BeldexSyncStatus()
            {
                Summary = pair.Value,
                PaymentMethodId = PaymentMethodId.Parse(pair.Key).ToString()
            });
        }
    }

    public class BeldexSyncStatus : SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get
            {
                return Summary?.WalletAvailable ?? false;
            }
        }

        public BeldexRPCProvider.BeldexLikeSummary Summary { get; set; }
    }
}