#nullable enable
using System.Globalization;

using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Beldex.Payments
{
    public class BeldexPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly BeldexLikeSpecificBtcPayNetwork _network;

        public BeldexPaymentLinkExtension(PaymentMethodId paymentMethodId, BeldexLikeSpecificBtcPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
        }
        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            return $"{_network.UriScheme}:{prompt.Destination}?tx_amount={due.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}