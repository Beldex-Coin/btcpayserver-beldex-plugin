using BTCPayServer.Tests;

using Xunit.Abstractions;

namespace BTCPayServer.Plugins.IntegrationTests.Beldex
{
    public class BeldexAndBitcoinIntegrationTestBase : UnitTestBase
    {

        public BeldexAndBitcoinIntegrationTestBase(ITestOutputHelper helper) : base(helper)
        {
            SetDefaultEnv("BTCPAY_BDX_DAEMON_URI", "http://127.0.0.1:18081");
            SetDefaultEnv("BTCPAY_BDX_WALLET_DAEMON_URI", "http://127.0.0.1:18082");
            SetDefaultEnv("BTCPAY_BDX_WALLET_DAEMON_WALLETDIR", "/wallet");
        }

        private static void SetDefaultEnv(string key, string defaultValue)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, defaultValue);
            }
        }
    }
}