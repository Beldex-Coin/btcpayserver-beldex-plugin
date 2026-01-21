namespace BTCPayServer.Plugins.Beldex.Payments
{
    public class BeldexPaymentPromptDetails
    {
        public long AccountIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}