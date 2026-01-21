using BTCPayServer.Payments;
using BTCPayServer.Plugins.Beldex.ViewModels;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.ViewModels
{
    public class BeldexPaymentViewModelTests
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void BeldexPaymentViewModel_SetGetProperties_ReturnsCorrectValues()
        {
            var viewModel = new BeldexPaymentViewModel();

            var paymentMethodId = new PaymentMethodId("BDX");

            var confirmations = "3";
            var depositAddress = "beldexaddress";
            var amount = "100.5";
            var transactionId = "tx123";
            var receivedTime = DateTimeOffset.UtcNow;
            var transactionLink = "https://explorer.beldex.io/tx/tx123";
            var currency = "BDX";

            viewModel.PaymentMethodId = paymentMethodId;
            viewModel.Confirmations = confirmations;
            viewModel.DepositAddress = depositAddress;
            viewModel.Amount = amount;
            viewModel.TransactionId = transactionId;
            viewModel.ReceivedTime = receivedTime;
            viewModel.TransactionLink = transactionLink;
            viewModel.Currency = currency;

            Assert.Equal(paymentMethodId, viewModel.PaymentMethodId);
            Assert.Equal(confirmations, viewModel.Confirmations);
            Assert.Equal(depositAddress, viewModel.DepositAddress);
            Assert.Equal(amount, viewModel.Amount);
            Assert.Equal(transactionId, viewModel.TransactionId);
            Assert.Equal(receivedTime, viewModel.ReceivedTime);
            Assert.Equal(transactionLink, viewModel.TransactionLink);
            Assert.Equal(currency, viewModel.Currency);
        }
    }
}