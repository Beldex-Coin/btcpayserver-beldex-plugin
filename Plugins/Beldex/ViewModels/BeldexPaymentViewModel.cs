using System;

using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Beldex.ViewModels
{
    public class BeldexPaymentViewModel
    {
        public PaymentMethodId PaymentMethodId { get; set; }
        public string Confirmations { get; set; }
        public string DepositAddress { get; set; }
        public string Amount { get; set; }
        public string TransactionId { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public string TransactionLink { get; set; }
        public string Currency { get; set; }
    }
}