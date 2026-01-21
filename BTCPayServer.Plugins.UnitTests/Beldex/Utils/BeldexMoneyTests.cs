using System.Globalization;

using BTCPayServer.Plugins.Beldex.Utils;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.Utils
{
    public class BeldexMoneyTests
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData(1, "0.000000001")]
        [InlineData(123456789, "0.123456789")]
        [InlineData(1000000000, "1.000000000")]
        public void Convert_LongToDecimal_ReturnsExpectedValue(long piconero, string expectedString)
        {
            decimal expected = decimal.Parse(expectedString, CultureInfo.InvariantCulture);
            decimal result = BeldexMoney.Convert(piconero);
            Assert.Equal(expected, result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("0.000000001", 1)]
        [InlineData("0.123456789", 123456789)]
        [InlineData("1.000000000", 1000000000)]
        public void Convert_DecimalToLong_ReturnsExpectedValue(string beldexString, long expectedPiconero)
        {
            decimal beldex = decimal.Parse(beldexString, CultureInfo.InvariantCulture);
            long result = BeldexMoney.Convert(beldex);
            Assert.Equal(expectedPiconero, result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData(1)]
        [InlineData(123456789)]
        [InlineData(1000000000)]
        public void RoundTripConversion_LongToDecimalToLong_ReturnsOriginalValue(long piconero)
        {
            decimal beldex = BeldexMoney.Convert(piconero);
            long convertedBack = BeldexMoney.Convert(beldex);
            Assert.Equal(piconero, convertedBack);
        }
    }
}