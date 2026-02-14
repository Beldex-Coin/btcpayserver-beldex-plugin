using System;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Beldex.RPC.Models;
using BTCPayServer.Plugins.Beldex.Services;
using BTCPayServer.Plugins.Beldex.Utils;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Beldex.Payments
{
    public class BeldexLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly BeldexLikeSpecificBtcPayNetwork _network;
        public BeldexLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly BeldexRpcProvider _beldexRpcProvider;

        public PaymentMethodId PaymentMethodId { get; }

        public BeldexLikePaymentMethodHandler(BeldexLikeSpecificBtcPayNetwork network, BeldexRpcProvider beldexRpcProvider)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _beldexRpcProvider = beldexRpcProvider;
        }
        bool IsReady() => _beldexRpcProvider.IsConfigured(_network.CryptoCode) && _beldexRpcProvider.IsAvailable(_network.CryptoCode);

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated && IsReady())
            {
                var supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                var walletClient = _beldexRpcProvider.WalletRpcClients[_network.CryptoCode];
                var daemonClient = _beldexRpcProvider.DaemonRpcClients[_network.CryptoCode];
                try
                {
                    context.State = new Prepare()
                    {
                        GetFeeRate = daemonClient.SendCommandAsync<GetFeeEstimateRequest, GetFeeEstimateResponse>("get_fee_estimate", new GetFeeEstimateRequest()),
                        ReserveAddress = s => walletClient.SendCommandAsync<CreateAddressRequest, CreateAddressResponse>("create_address", new CreateAddressRequest() { Label = $"btcpay invoice #{s}", AccountIndex = supportedPaymentMethod.AccountIndex }),
                        AccountIndex = supportedPaymentMethod.AccountIndex
                    };
                }
                catch (Exception ex)
                {
                    context.Logs.Write($"Error in BeforeFetchingRates: {ex.Message}", InvoiceEventData.EventSeverity.Error);
                }
            }
            return Task.CompletedTask;
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_beldexRpcProvider.IsConfigured(_network.CryptoCode))
            {
                throw new PaymentMethodUnavailableException($"BTCPAY_BDX_WALLET_DAEMON_URI or BTCPAY_BDX_DAEMON_URI isn't configured");
            }

            if (!_beldexRpcProvider.IsAvailable(_network.CryptoCode) || context.State is not Prepare beldexPrepare)
            {
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            }

            var invoice = context.InvoiceEntity;
            var feeRate = await beldexPrepare.GetFeeRate;
            var address = await beldexPrepare.ReserveAddress(invoice.Id);

            var feeRatePerByte = feeRate.FeePerByte;
            var details = new BeldexLikeOnChainPaymentMethodDetails()
            {
                AccountIndex = beldexPrepare.AccountIndex,
                AddressIndex = address.AddressIndex,
                InvoiceSettledConfirmationThreshold = ParsePaymentMethodConfig(context.PaymentMethodConfig).InvoiceSettledConfirmationThreshold
            };
            context.Prompt.Destination = address.Address;
            context.Prompt.PaymentMethodFee = BeldexMoney.Convert(feeRatePerByte * 100);
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(address.Address);
        }
        private BeldexPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<BeldexPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(BeldexLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        class Prepare
        {
            public Task<GetFeeEstimateResponse> GetFeeRate;
            public Func<string, Task<CreateAddressResponse>> ReserveAddress;

            public long AccountIndex { get; internal set; }
        }

        public BeldexLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<BeldexLikeOnChainPaymentMethodDetails>(Serializer);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }

        public BeldexLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<BeldexLikePaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(BeldexLikePaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}