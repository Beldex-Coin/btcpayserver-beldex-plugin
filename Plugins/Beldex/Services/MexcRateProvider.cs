using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Beldex.Services
{
    /// <summary>
    /// Rate provider backed by the MEXC public book ticker.
    /// BDX is no longer tradeable on Kraken, and no exchange shipped with BTCPay Server
    /// lists a liquid BDX market, so the plugin brings its own source.
    /// One unauthenticated request returns every spot market; only pairs quoted in a
    /// currency BTCPay can chain to (USDT, USDC, BTC, ETH) are kept.
    /// </summary>
    public class MexcRateProvider : IRateProvider
    {
        private const string Endpoint = "https://api.mexc.com/api/v3/ticker/bookTicker";

        // Longest first so "USDT" is tested before "BTC"/"ETH".
        private static readonly string[] QuoteCurrencies = { "USDT", "USDC", "BTC", "ETH" };

        private readonly HttpClient _httpClient;

        public MexcRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public RateSourceInfo RateSourceInfo => new("mexc", "MEXC", Endpoint);

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(Endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseTickers(JArray.Parse(json));
        }

        internal static PairRate[] ParseTickers(JArray tickers)
        {
            var result = new List<PairRate>();
            foreach (var ticker in tickers.Children<JObject>())
            {
                var symbol = ticker["symbol"]?.Value<string>();
                if (string.IsNullOrEmpty(symbol))
                {
                    continue;
                }
                if (!TryParsePair(symbol, out var pair))
                {
                    continue;
                }
                if (!TryGetDecimal(ticker["bidPrice"], out var bid) || !TryGetDecimal(ticker["askPrice"], out var ask))
                {
                    continue;
                }
                // MEXC reports 0 for markets without a resting order; such a quote is unusable.
                if (bid <= 0m || ask <= 0m || bid > ask)
                {
                    continue;
                }
                result.Add(new PairRate(pair, new BidAsk(bid, ask)));
            }
            return result.ToArray();
        }

        private static bool TryParsePair(string symbol, out CurrencyPair pair)
        {
            pair = null;
            foreach (var quote in QuoteCurrencies)
            {
                if (symbol.Length <= quote.Length || !symbol.EndsWith(quote, StringComparison.Ordinal))
                {
                    continue;
                }
                pair = new CurrencyPair(symbol.Substring(0, symbol.Length - quote.Length), quote);
                return true;
            }
            return false;
        }

        private static bool TryGetDecimal(JToken token, out decimal value)
        {
            value = 0m;
            return token is { Type: JTokenType.String or JTokenType.Float or JTokenType.Integer }
                && decimal.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
