using System.Globalization;

namespace BTCPayServer.Plugins.Beldex.Utils
{
    public static class BeldexMoney
    {
        public static decimal Convert(long piconero)
        {
            var amt = piconero.ToString(CultureInfo.InvariantCulture).PadLeft(9, '0');
            amt = amt.Length == 9 ? $"0.{amt}" : amt.Insert(amt.Length - 9, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal beldex)
        {
            return System.Convert.ToInt64(beldex * 1000000000);
        }
    }
}