namespace BTCPayServer.Plugins.Beldex.Payments
{
    public class BeldexLikeOnChainPaymentMethodDetails
    {
        public long AccountIndex { get; set; }
        public long AddressIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}