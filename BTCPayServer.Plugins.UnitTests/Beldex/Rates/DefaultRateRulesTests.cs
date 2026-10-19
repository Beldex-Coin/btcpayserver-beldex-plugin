using System.Linq;

using BTCPayServer.Plugins.Beldex;
using BTCPayServer.Rating;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.Rates
{
    public class DefaultRateRulesTests
    {
        // Mirrors how BTCPay Server consumes BTCPayNetworkBase.DefaultRateRules:
        // DefaultRules(string[]) parses each rule and combines them (BTCPayServer/DefaultRules.cs).
        private static RateRules ParseDefaults()
        {
            return RateRules.Combine(BeldexPlugin.DefaultBdxRateRules.Select(RateRules.Parse));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DefaultRules_EachRule_ParsesWithoutErrors()
        {
            foreach (var rule in BeldexPlugin.DefaultBdxRateRules)
            {
                Assert.True(RateRules.TryParse(rule, out _, out var errors), $"Rule '{rule}' failed to parse: {string.Join(",", errors ?? [])}");
            }
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DefaultRules_DoNotReferenceKrakenForBdx()
        {
            // Kraken delisted BDX; no rule may try to fetch a BDX pair from it.
            Assert.DoesNotContain(BeldexPlugin.DefaultBdxRateRules, r => r.Contains("kraken(BDX"));
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("BDX_BTC", "mexc(BDX_USDT) / mexc(BTC_USDT)", "mexc(BDX_USDT),mexc(BTC_USDT)")]
        [InlineData("BDX_USD", "(mexc(BDX_USDT) / mexc(BTC_USDT)) * kraken(BTC_USD)", "mexc(BDX_USDT),mexc(BTC_USDT),kraken(BTC_USD)")]
        [InlineData("BDX_EUR", "(mexc(BDX_USDT) / mexc(BTC_USDT)) * kraken(BTC_EUR)", "mexc(BDX_USDT),mexc(BTC_USDT),kraken(BTC_EUR)")]
        public void DefaultRules_ResolveToValidExpressions(string pair, string expectedExpression, string expectedSources)
        {
            var rules = ParseDefaults();

            var rule = rules.GetRuleFor(CurrencyPair.Parse(pair));

            Assert.Equal(expectedExpression, rule.ToString());
            Assert.Equal(expectedSources, string.Join(',', rule.ExchangeRates.OfType<object>()));
            Assert.DoesNotContain("ERR_", rule.ToString());
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DefaultRules_BdxUsd_EvaluatesFromSourceRates()
        {
            var rules = ParseDefaults();
            var rule = rules.GetRuleFor(CurrencyPair.Parse("BDX_USD"));

            // Quotes observed live on 2026-10-19.
            rule.ExchangeRates.SetRate("mexc", CurrencyPair.Parse("BDX_USDT"), new BidAsk(0.07428m, 0.07435m));
            rule.ExchangeRates.SetRate("mexc", CurrencyPair.Parse("BTC_USDT"), new BidAsk(81968.70m, 81968.71m));
            rule.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(80951.5m, 80951.6m));

            Assert.True(rule.Reevaluate(), string.Join(",", rule.Errors));
            Assert.False(rule.HasError);
            Assert.NotNull(rule.BidAsk);
            Assert.InRange(rule.BidAsk.Bid, 0.070m, 0.080m);
            Assert.True(rule.BidAsk.Ask >= rule.BidAsk.Bid);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DefaultRules_BdxUsd_FailsClearlyWhenMexcUnavailable()
        {
            var rules = ParseDefaults();
            var rule = rules.GetRuleFor(CurrencyPair.Parse("BDX_USD"));
            rule.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(80951.5m, 80951.6m));

            Assert.False(rule.Reevaluate());
            Assert.Contains(RateRulesErrors.RateUnavailable, rule.Errors);
            Assert.Contains("ERR_RATE_UNAVAILABLE(mexc, BDX_USDT)", rule.ToString(true));
        }
    }
}
