using BTCPayServer.Plugins.Beldex.Configuration;

using Xunit;

namespace BTCPayServer.Plugins.UnitTests.Beldex.Configuration
{
    public class BeldexLikeConfigurationTests
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void BeldexLikeConfiguration_ShouldInitializeWithEmptyDictionary()
        {
            var config = new BeldexLikeConfiguration();

            Assert.NotNull(config.BeldexLikeConfigurationItems);
            Assert.Empty(config.BeldexLikeConfigurationItems);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void BeldexLikeConfigurationItem_ShouldSetAndGetProperties()
        {
            var configItem = new BeldexLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081"),
                InternalWalletRpcUri = new Uri("http://localhost:18082"),
                WalletDirectory = "/wallets",
                Username = "user",
                Password = "password"
            };

            Assert.Equal("http://localhost:18081/", configItem.DaemonRpcUri.ToString());
            Assert.Equal("http://localhost:18082/", configItem.InternalWalletRpcUri.ToString());
            Assert.Equal("/wallets", configItem.WalletDirectory);
            Assert.Equal("user", configItem.Username);
            Assert.Equal("password", configItem.Password);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void BeldexLikeConfiguration_ShouldAddAndRetrieveItems()
        {
            var config = new BeldexLikeConfiguration();
            var configItem = new BeldexLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081"),
                InternalWalletRpcUri = new Uri("http://localhost:18082"),
                WalletDirectory = "/wallets",
                Username = "user",
                Password = "password"
            };

            config.BeldexLikeConfigurationItems.Add("BDX", configItem);

            Assert.Single(config.BeldexLikeConfigurationItems);
            Assert.True(config.BeldexLikeConfigurationItems.ContainsKey("BDX"));
            Assert.Equal(configItem, config.BeldexLikeConfigurationItems["BDX"]);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void BeldexLikeConfiguration_ShouldHandleDuplicateKeys()
        {
            var config = new BeldexLikeConfiguration();
            var configItem1 = new BeldexLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18081")
            };
            var configItem2 = new BeldexLikeConfigurationItem
            {
                DaemonRpcUri = new Uri("http://localhost:18082")
            };

            config.BeldexLikeConfigurationItems.Add("BDX", configItem1);

            Assert.Throws<ArgumentException>(() =>
                config.BeldexLikeConfigurationItems.Add("BDX", configItem2));
        }
    }
}