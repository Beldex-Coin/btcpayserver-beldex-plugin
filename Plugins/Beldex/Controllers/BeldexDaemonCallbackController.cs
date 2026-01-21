using BTCPayServer.Plugins.Beldex.RPC;

using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Beldex.Controllers
{
    [Route("[controller]")]
    public class BeldexLikeDaemonCallbackController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public BeldexLikeDaemonCallbackController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }
        [HttpGet("block")]
        public IActionResult OnBlockNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new BeldexEvent()
            {
                BlockHash = hash,
                CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }
        [HttpGet("tx")]
        public IActionResult OnTransactionNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new BeldexEvent()
            {
                TransactionHash = hash,
                CryptoCode = cryptoCode.ToUpperInvariant()
            });
            return Ok();
        }

    }
}