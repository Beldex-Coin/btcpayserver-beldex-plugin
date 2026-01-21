using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Beldex.Configuration;
using BTCPayServer.Plugins.Beldex.Payments;
using BTCPayServer.Plugins.Beldex.RPC.Models;
using BTCPayServer.Plugins.Beldex.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Beldex.Controllers
{
    [Route("stores/{storeId}/beldexlike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIBeldexLikeStoreController : Controller
    {
        private readonly BeldexLikeConfiguration _BeldexLikeConfiguration;
        private readonly StoreRepository _StoreRepository;
        private readonly BeldexRPCProvider _BeldexRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private IStringLocalizer StringLocalizer { get; }

        public UIBeldexLikeStoreController(BeldexLikeConfiguration beldexLikeConfiguration,
            StoreRepository storeRepository, BeldexRPCProvider beldexRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer)
        {
            _BeldexLikeConfiguration = beldexLikeConfiguration;
            _StoreRepository = storeRepository;
            _BeldexRpcProvider = beldexRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public async Task<IActionResult> GetStoreBeldexLikePaymentMethods()
        {
            return View("/Views/Beldex/GetStoreBeldexLikePaymentMethods.cshtml", await GetVM(StoreData));
        }
        [NonAction]
        public async Task<BeldexLikePaymentMethodListViewModel> GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            var accountsList = _BeldexLikeConfiguration.BeldexLikeConfigurationItems.ToDictionary(pair => pair.Key,
                pair => GetAccounts(pair.Key));

            await Task.WhenAll(accountsList.Values);
            return new BeldexLikePaymentMethodListViewModel()
            {
                Items = _BeldexLikeConfiguration.BeldexLikeConfigurationItems.Select(pair =>
                    GetBeldexLikePaymentMethodViewModel(storeData, pair.Key, excludeFilters,
                        accountsList[pair.Key].Result))
            };
        }

        private Task<GetAccountsResponse> GetAccounts(string cryptoCode)
        {
            try
            {
                if (_BeldexRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary) && summary.WalletAvailable)
                {

                    return _BeldexRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GetAccountsRequest, GetAccountsResponse>("get_accounts", new GetAccountsRequest());
                }
            }
            catch
            {
                // ignored
            }

            return Task.FromResult<GetAccountsResponse>(null);
        }

        private BeldexLikePaymentMethodViewModel GetBeldexLikePaymentMethodViewModel(
            StoreData storeData, string cryptoCode,
            IPaymentFilter excludeFilters, GetAccountsResponse accountsResponse)
        {
            var beldex = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is BeldexPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (BeldexPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = beldex.Where(method => method.PaymentMethodId == pmi).Select(m => m.Details).SingleOrDefault();
            _BeldexRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);
            _BeldexLikeConfiguration.BeldexLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem);
            var accounts = accountsResponse?.SubaddressAccounts?.Select(account =>
                new SelectListItem(
                    $"{account.AccountIndex} - {(string.IsNullOrEmpty(account.Label) ? "No label" : account.Label)}",
                    account.AccountIndex.ToString(CultureInfo.InvariantCulture)));

            var settlementThresholdChoice = BeldexLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => BeldexLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => BeldexLikeSettlementThresholdChoice.AtLeastOne,
                    10 => BeldexLikeSettlementThresholdChoice.AtLeastTen,
                    _ => BeldexLikeSettlementThresholdChoice.Custom
                };
            }

            return new BeldexLikePaymentMethodViewModel()
            {
                Enabled =
                    settings != null &&
                    !excludeFilters.Match(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)),
                Summary = summary,
                CryptoCode = cryptoCode,
                AccountIndex = settings?.AccountIndex ?? accountsResponse?.SubaddressAccounts?.FirstOrDefault()?.AccountIndex ?? 0,
                Accounts = accounts == null ? null : new SelectList(accounts, nameof(SelectListItem.Value),
                    nameof(SelectListItem.Text)),
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is BeldexLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public async Task<IActionResult> GetStoreBeldexLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_BeldexLikeConfiguration.BeldexLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetBeldexLikePaymentMethodViewModel(StoreData, cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));
            return View("/Views/Beldex/GetStoreBeldexLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreBeldexLikePaymentMethod(BeldexLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_BeldexLikeConfiguration.BeldexLikeConfigurationItems.TryGetValue(cryptoCode,
                out var configurationItem))
            {
                return NotFound();
            }

            if (command == "add-account")
            {
                try
                {
                    var newAccount = await _BeldexRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<CreateAccountRequest, CreateAccountResponse>("create_account", new CreateAccountRequest()
                    {
                        Label = viewModel.NewAccountLabel
                    });
                    viewModel.AccountIndex = newAccount.AccountIndex;
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not create a new account."]);
                }

            }
            else if (command == "set-wallet-details")
            {
                var valid = true;
                if (viewModel.PrimaryAddress == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["Please set your primary public address"]);
                    valid = false;
                }
                if (viewModel.PrivateViewKey == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrivateViewKey), StringLocalizer["Please set your private view key"]);
                    valid = false;
                }
                if (configurationItem.WalletDirectory == null)
                {
                    ModelState.AddModelError(nameof(viewModel.PrimaryAddress), StringLocalizer["This installation doesn't support wallet creation (BTCPAY_BDX_WALLET_DAEMON_WALLETDIR is not set)"]);
                    valid = false;
                }
                if (valid)
                {
                    if (_BeldexRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary))
                    {
                        if (summary.WalletAvailable)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel
                            {
                                Severity = StatusMessageModel.StatusSeverity.Error,
                                Message = StringLocalizer["There is already an active wallet configured for {0}. Replacing it would break any existing invoices!", cryptoCode].Value
                            });
                            return RedirectToAction(nameof(GetStoreBeldexLikePaymentMethod),
                                new { cryptoCode });
                        }
                    }
                    try
                    {
                        var response = await _BeldexRpcProvider.WalletRpcClients[cryptoCode].SendCommandAsync<GenerateFromKeysRequest, GenerateFromKeysResponse>("generate_from_keys", new GenerateFromKeysRequest
                        {
                            PrimaryAddress = viewModel.PrimaryAddress,
                            PrivateViewKey = viewModel.PrivateViewKey,
                            WalletFileName = "view_wallet",
                            RestoreHeight = viewModel.RestoreHeight,
                            Password = viewModel.WalletPassword
                        });
                        if (response?.Error != null)
                        {
                            throw new GenerateFromKeysException(response.Error.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError(nameof(viewModel.AccountIndex), StringLocalizer["Could not generate view wallet from keys: {0}", ex.Message]);
                        return View("/Views/Beldex/GetStoreBeldexLikePaymentMethod.cshtml", viewModel);
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Info,
                        Message = StringLocalizer["View-only wallet created. The wallet will soon become available."].Value
                    });
                    return RedirectToAction(nameof(GetStoreBeldexLikePaymentMethod), new { cryptoCode });
                }
            }

            if (!ModelState.IsValid)
            {

                var vm = GetBeldexLikePaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods(), await GetAccounts(cryptoCode));

                vm.Enabled = viewModel.Enabled;
                vm.NewAccountLabel = viewModel.NewAccountLabel;
                vm.AccountIndex = viewModel.AccountIndex;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Beldex/GetStoreBeldexLikePaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(_handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)], new BeldexPaymentPromptDetails()
            {
                AccountIndex = viewModel.AccountIndex,
                InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
                {
                    BeldexLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                    BeldexLikeSettlementThresholdChoice.AtLeastOne => 1,
                    BeldexLikeSettlementThresholdChoice.AtLeastTen => 10,
                    BeldexLikeSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                    _ => null
                }
            });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode), !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _StoreRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreBeldexLikePaymentMethods",
                new { StatusMessage = $"{cryptoCode} settings updated successfully", storeId = StoreData.Id });
        }

        public class BeldexLikePaymentMethodListViewModel
        {
            public IEnumerable<BeldexLikePaymentMethodViewModel> Items { get; set; }
        }

        public class BeldexLikePaymentMethodViewModel : IValidatableObject
        {
            public BeldexRPCProvider.BeldexLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public string NewAccountLabel { get; set; }
            public long AccountIndex { get; set; }
            public bool Enabled { get; set; }

            public IEnumerable<SelectListItem> Accounts { get; set; }
            public bool WalletFileFound { get; set; }
            [Display(Name = "Primary Public Address")]
            public string PrimaryAddress { get; set; }
            [Display(Name = "Private View Key")]
            public string PrivateViewKey { get; set; }
            [Display(Name = "Restore Height")]
            public int RestoreHeight { get; set; }
            [Display(Name = "Wallet Password")]
            public string WalletPassword { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public BeldexLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is BeldexLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum BeldexLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}