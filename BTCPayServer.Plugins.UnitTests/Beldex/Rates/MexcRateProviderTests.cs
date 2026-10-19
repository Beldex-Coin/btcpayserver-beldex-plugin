using System.Net;
using System.Text;

using BTCPayServer.Plugins.Beldex.Services;
using BTCPayServer.Rating;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.Rates
{
    public class MexcRateProviderTests
    {
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;
            public string RequestedUrl { get; private set; } = "";
            public StubHandler(string body) { _body = body; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestedUrl = request.RequestUri!.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }

        // Shape of https://api.mexc.com/api/v3/ticker/bookTicker as observed on 2026-10-19.
        private const string SampleBody = """
            [
              {"symbol":"BDXUSDT","bidPrice":"0.07428","bidQty":"17.69","askPrice":"0.07435","askQty":"17.66"},
              {"symbol":"BTCUSDT","bidPrice":"81968.70","bidQty":"0.12309882","askPrice":"81968.71","askQty":"3.75263200"},
              {"symbol":"ETHBTC","bidPrice":"0.0381","bidQty":"1","askPrice":"0.0382","askQty":"1"},
              {"symbol":"DEADUSDT","bidPrice":"0","bidQty":"0","askPrice":"0","askQty":"0"},
              {"symbol":"ABCXYZ","bidPrice":"1","bidQty":"1","askPrice":"2","askQty":"1"},
              {"symbol":"","bidPrice":"1","bidQty":"1","askPrice":"2","askQty":"1"}
            ]
            """;

        [Trait("Category", "Unit")]
        [Fact]
        public void RateSourceInfo_IsMexc()
        {
            var provider = new MexcRateProvider(new HttpClient(new StubHandler("[]")));

            Assert.Equal("mexc", provider.RateSourceInfo.Id);
            Assert.Equal("MEXC", provider.RateSourceInfo.DisplayName);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task GetRatesAsync_ParsesSupportedPairsAndSkipsUnusableOnes()
        {
            var handler = new StubHandler(SampleBody);
            var provider = new MexcRateProvider(new HttpClient(handler));

            var rates = await provider.GetRatesAsync(CancellationToken.None);

            Assert.Equal("https://api.mexc.com/api/v3/ticker/bookTicker", handler.RequestedUrl);
            Assert.Equal(3, rates.Length);

            var bdx = Assert.Single(rates, r => r.CurrencyPair == CurrencyPair.Parse("BDX_USDT"));
            Assert.Equal(0.07428m, bdx.BidAsk.Bid);
            Assert.Equal(0.07435m, bdx.BidAsk.Ask);

            var btc = Assert.Single(rates, r => r.CurrencyPair == CurrencyPair.Parse("BTC_USDT"));
            Assert.Equal(81968.70m, btc.BidAsk.Bid);
            Assert.Equal(81968.71m, btc.BidAsk.Ask);

            Assert.Single(rates, r => r.CurrencyPair == CurrencyPair.Parse("ETH_BTC"));
            Assert.DoesNotContain(rates, r => r.CurrencyPair.Left == "DEAD" || r.CurrencyPair.Left == "ABC");
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task GetRatesAsync_EmptyArray_ReturnsNoRates()
        {
            var provider = new MexcRateProvider(new HttpClient(new StubHandler("[]")));

            var rates = await provider.GetRatesAsync(CancellationToken.None);

            Assert.Empty(rates);
        }
    }
}
