using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LiquidityProvider
{
    [Serializable]
    public class Configuration
    {
        public string AccountKey { get; set; } = "";
        public string EtherscanAPIKey { get; set; } = "";

        public string TelegramAPI { get; set; } = "";
        public long NotifyUserID { get; set; } = 0;
        public BigInteger GasLimitWei { get; set; } = 15000000;
    }
}
