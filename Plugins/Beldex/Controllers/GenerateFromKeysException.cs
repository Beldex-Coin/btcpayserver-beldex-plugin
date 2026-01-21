using System;

namespace BTCPayServer.Plugins.Beldex.Controllers;

public class GenerateFromKeysException(string message) : Exception(message);