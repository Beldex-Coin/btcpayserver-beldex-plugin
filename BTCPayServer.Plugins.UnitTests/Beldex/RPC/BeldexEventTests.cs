using BTCPayServer.Plugins.Beldex.RPC;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.RPC
{
    public class BeldexEventTest
    {
        [Fact]
        public void DefaultInitialization_ShouldHaveNullProperties()
        {
            var beldexEvent = new BeldexEvent();

            Assert.Null(beldexEvent.BlockHash);
            Assert.Null(beldexEvent.TransactionHash);
            Assert.Null(beldexEvent.CryptoCode);
        }

        [Fact]
        public void PropertyAssignment_ShouldSetAndRetrieveValues()
        {
            var beldexEvent = new BeldexEvent
            {
                BlockHash = "block123",
                TransactionHash = "tx456",
                CryptoCode = "BDX"
            };

            Assert.Equal("block123", beldexEvent.BlockHash);
            Assert.Equal("tx456", beldexEvent.TransactionHash);
            Assert.Equal("BDX", beldexEvent.CryptoCode);
        }

        [Theory]
        [InlineData("block123", "tx456", "BDX", "BDX: Tx Update New Block (tx456block123)")]
        public void ToString_ShouldReturnCorrectString(string blockHash, string transactionHash, string cryptoCode, string expected)
        {
            var beldexEvent = new BeldexEvent
            {
                BlockHash = blockHash,
                TransactionHash = transactionHash,
                CryptoCode = cryptoCode
            };

            var result = beldexEvent.ToString();

            Assert.Equal(expected, result);
        }


    }
}