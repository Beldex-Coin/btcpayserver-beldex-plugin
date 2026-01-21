using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Beldex.Configuration;
using BTCPayServer.Plugins.Beldex.Payments;
using BTCPayServer.Plugins.Beldex.RPC;
using BTCPayServer.Plugins.Beldex.RPC.Models;
using BTCPayServer.Plugins.Beldex.Utils;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;

using Microsoft.Extensions.Logging;

using NBitcoin;

using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Beldex.Services
{
    public class BeldexListener : EventHostedServiceBase
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly BeldexRPCProvider _beldexRpcProvider;
        private readonly BeldexLikeConfiguration _BeldexLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<BeldexListener> _logger;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly PaymentService _paymentService;

        public BeldexListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BeldexRPCProvider beldexRpcProvider,
            BeldexLikeConfiguration beldexLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<BeldexListener> logger,
            PaymentMethodHandlerDictionary handlers,
            InvoiceActivator invoiceActivator,
            PaymentService paymentService) : base(eventAggregator, logger)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _beldexRpcProvider = beldexRpcProvider;
            _BeldexLikeConfiguration = beldexLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _handlers = handlers;
            _invoiceActivator = invoiceActivator;
            _paymentService = paymentService;
        }

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Subscribe<BeldexEvent>();
            Subscribe<BeldexRPCProvider.BeldexDaemonStateChange>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is BeldexRPCProvider.BeldexDaemonStateChange stateChange)
            {
                if (_beldexRpcProvider.IsAvailable(stateChange.CryptoCode))
                {
                    _logger.LogInformation($"{stateChange.CryptoCode} just became available");
                    _ = UpdateAnyPendingBeldexLikePayment(stateChange.CryptoCode);
                }
                else
                {
                    _logger.LogInformation($"{stateChange.CryptoCode} just became unavailable");
                }
            }
            else if (evt is BeldexEvent beldexEvent)
            {
                if (!_beldexRpcProvider.IsAvailable(beldexEvent.CryptoCode))
                {
                    return;
                }

                if (!string.IsNullOrEmpty(beldexEvent.BlockHash))
                {
                    await OnNewBlock(beldexEvent.CryptoCode);
                }

                if (!string.IsNullOrEmpty(beldexEvent.TransactionHash))
                {
                    await OnTransactionUpdated(beldexEvent.CryptoCode, beldexEvent.TransactionHash);
                }
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);

            if (prompt != null &&
                prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
                invoice = await _invoiceRepository.GetInvoice(invoice.Id);
            }

            _eventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices)
        {
            if (!invoices.Any())
            {
                return;
            }

            var beldexWalletRpcClient = _beldexRpcProvider.WalletRpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);
            var paymentId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (BeldexLikePaymentMethodHandler)_handlers[paymentId];

            //get all the required data in one list (invoice, its existing payments and the current payment method details)
            var expandedInvoices = invoices.Select(entity => (Invoice: entity,
                    ExistingPayments: GetAllBeldexLikePayments(entity, cryptoCode),
                    Prompt: entity.GetPaymentPrompt(paymentId),
                    PaymentMethodDetails: handler.ParsePaymentPromptDetails(entity.GetPaymentPrompt(paymentId)
                        .Details)))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    tuple.Prompt,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity, PaymentData: handler.ParsePaymentDetails(entity.Details),
                            tuple.Invoice))
                ));

            var existingPaymentData = expandedInvoices.SelectMany(tuple => tuple.ExistingPayments);

            var accountToAddressQuery = new Dictionary<long, List<long>>();
            //create list of subaddresses to account to query the beldex wallet
            foreach (var expandedInvoice in expandedInvoices)
            {
                var addressIndexList =
                    accountToAddressQuery.GetValueOrDefault(expandedInvoice.PaymentMethodDetails.AccountIndex, []);

                addressIndexList.AddRange(
                    expandedInvoice.ExistingPayments.Select(tuple => tuple.PaymentData.SubaddressIndex));
                addressIndexList.Add(expandedInvoice.PaymentMethodDetails.AddressIndex);
                accountToAddressQuery.AddOrReplace(expandedInvoice.PaymentMethodDetails.AccountIndex, addressIndexList);
            }

            var tasks = accountToAddressQuery.ToDictionary(datas => datas.Key,
                datas => beldexWalletRpcClient.SendCommandAsync<GetTransfersRequest, GetTransfersResponse>(
                    "get_transfers",
                    new GetTransfersRequest()
                    {
                        AccountIndex = datas.Key,
                        In = true,
                        SubaddrIndices = datas.Value.Distinct().ToList()
                    }));

            await Task.WhenAll(tasks.Values);


            var transferProcessingTasks = new List<Task>();

            var updatedPaymentEntities = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();
            foreach (var keyValuePair in tasks)
            {
                var transfers = keyValuePair.Value.Result.In;
                if (transfers == null)
                {
                    continue;
                }

                transferProcessingTasks.AddRange(transfers.Select(transfer =>
                {
                    InvoiceEntity invoice = null;
                    var existingMatch = existingPaymentData.SingleOrDefault(tuple =>
                        tuple.Payment.Destination == transfer.Address &&
                        tuple.PaymentData.TransactionId == transfer.Txid);

                    if (existingMatch.Invoice != null)
                    {
                        invoice = existingMatch.Invoice;
                    }
                    else
                    {
                        var newMatch = expandedInvoices.SingleOrDefault(tuple =>
                            tuple.Prompt.Destination == transfer.Address);

                        if (newMatch.Invoice == null)
                        {
                            return Task.CompletedTask;
                        }

                        invoice = newMatch.Invoice;
                    }


                    return HandlePaymentData(cryptoCode, transfer.Amount, transfer.SubaddrIndex.Major,
                        transfer.SubaddrIndex.Minor, transfer.Txid, transfer.Confirmations, transfer.Height,
                        transfer.UnlockTime, invoice,
                        updatedPaymentEntities);
                }));
            }

            transferProcessingTasks.Add(
                _paymentService.UpdatePayments(updatedPaymentEntities.Select(tuple => tuple.Item1).ToList()));
            await Task.WhenAll(transferProcessingTasks);
            foreach (var valueTuples in updatedPaymentEntities.GroupBy(entity => entity.Item2))
            {
                if (valueTuples.Any())
                {
                    _eventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                }
            }
        }

        private async Task OnNewBlock(string cryptoCode)
        {
            await UpdateAnyPendingBeldexLikePayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent()
            { PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode) });
        }

        private async Task OnTransactionUpdated(string cryptoCode, string transactionHash)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var transfer = await GetTransferByTxId(cryptoCode, transactionHash, this.CancellationToken);
            if (transfer is null)
            {
                return;
            }
            var paymentsToUpdate = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();

            //group all destinations of the tx together and loop through the sets
            foreach (var destination in transfer.Transfers.GroupBy(destination => destination.Address))
            {
                //find the invoice corresponding to this address, else skip
                var invoice = await _invoiceRepository.GetInvoiceFromAddress(paymentMethodId, destination.Key);
                if (invoice == null)
                {
                    continue;
                }

                var index = destination.First().SubaddrIndex;

                await HandlePaymentData(cryptoCode,
                    destination.Sum(destination1 => destination1.Amount),
                    index.Major,
                    index.Minor,
                    transfer.Transfer.Txid,
                    transfer.Transfer.Confirmations,
                    transfer.Transfer.Height
                    , transfer.Transfer.UnlockTime, invoice, paymentsToUpdate);
            }

            if (paymentsToUpdate.Any())
            {
                await _paymentService.UpdatePayments(paymentsToUpdate.Select(tuple => tuple.Payment).ToList());
                foreach (var valueTuples in paymentsToUpdate.GroupBy(entity => entity.invoice))
                {
                    if (valueTuples.Any())
                    {
                        _eventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
                    }
                }
            }
        }

        private async Task<GetTransferByTransactionIdResponse> GetTransferByTxId(string cryptoCode,
            string transactionHash, CancellationToken cancellationToken)
        {
            var accounts = await _beldexRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest(), cancellationToken);
            var accountIndexes = accounts
                .SubaddressAccounts
                .Select(a => new long?(a.AccountIndex))
                .ToList();
            if (accountIndexes.Count is 0)
            {
                accountIndexes.Add(null);
            }
            var req = accountIndexes
                .Select(i => GetTransferByTxId(cryptoCode, transactionHash, i))
                .ToArray();
            foreach (var task in req)
            {
                var result = await task;
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private async Task<GetTransferByTransactionIdResponse> GetTransferByTxId(string cryptoCode, string transactionHash, long? accountIndex)
        {
            try
            {
                var result = await _beldexRpcProvider.WalletRpcClients[cryptoCode]
                    .SendCommandAsync<GetTransferByTransactionIdRequest, GetTransferByTransactionIdResponse>(
                        "get_transfer_by_txid",
                        new GetTransferByTransactionIdRequest()
                        {
                            TransactionId = transactionHash,
                            AccountIndex = accountIndex
                        });
                return result;
            }
            catch (JsonRpcClient.JsonRpcApiException)
            {
                return null;
            }
        }

        private async Task HandlePaymentData(string cryptoCode, long totalAmount, long subaccountIndex,
            long subaddressIndex,
            string txId, long confirmations, long blockHeight, long locktime, InvoiceEntity invoice,
            List<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (BeldexLikePaymentMethodHandler)_handlers[pmi];
            var promptDetails = handler.ParsePaymentPromptDetails(invoice.GetPaymentPrompt(pmi).Details);
            var details = new BeldexLikePaymentData()
            {
                SubaccountIndex = subaccountIndex,
                SubaddressIndex = subaddressIndex,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                BlockHeight = blockHeight,
                LockTime = locktime,
                InvoiceSettledConfirmationThreshold = promptDetails.InvoiceSettledConfirmationThreshold
            };
            var status = GetStatus(details, invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing;
            var paymentData = new PaymentData()
            {
                Status = status,
                Amount = BeldexMoney.Convert(totalAmount),
                Created = DateTimeOffset.UtcNow,
                Id = $"{txId}#{subaccountIndex}#{subaddressIndex}",
                Currency = network.CryptoCode,
                InvoiceDataId = invoice.Id,
            }.Set(invoice, handler, details);


            //check if this tx exists as a payment to this invoice already
            var alreadyExistingPaymentThatMatches = GetAllBeldexLikePayments(invoice, cryptoCode)
                .SingleOrDefault(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);

            //if it doesnt, add it and assign a new beldexlike address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches == null)
            {
                var payment = await _paymentService.AddPayment(paymentData, [txId]);
                if (payment != null)
                {
                    await ReceivedPayment(invoice, payment);
                }
            }
            else
            {
                //else update it with the new data
                alreadyExistingPaymentThatMatches.Status = status;
                alreadyExistingPaymentThatMatches.Details = JToken.FromObject(details, handler.Serializer);
                paymentsToUpdate.Add((alreadyExistingPaymentThatMatches, invoice));
            }
        }

        private bool GetStatus(BeldexLikePaymentData details, SpeedPolicy speedPolicy)
            => ConfirmationsRequired(details, speedPolicy) <= details.ConfirmationCount;

        public static long ConfirmationsRequired(BeldexLikePaymentData details, SpeedPolicy speedPolicy)
            => (details, speedPolicy) switch
            {
                (_, _) when details.ConfirmationCount < details.LockTime =>
                    details.LockTime - details.ConfirmationCount,
                ({ InvoiceSettledConfirmationThreshold: long v }, _) => v,
                (_, SpeedPolicy.HighSpeed) => 0,
                (_, SpeedPolicy.MediumSpeed) => 1,
                (_, SpeedPolicy.LowMediumSpeed) => 2,
                (_, SpeedPolicy.LowSpeed) => 6,
                _ => 6,
            };


        private async Task UpdateAnyPendingBeldexLikePayment(string cryptoCode)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var invoices = await _invoiceRepository.GetMonitoredInvoices(paymentMethodId);
            if (!invoices.Any())
            {
                return;
            }
            invoices = invoices.Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true).ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllBeldexLikePayments(InvoiceEntity invoice, string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        }
    }
}