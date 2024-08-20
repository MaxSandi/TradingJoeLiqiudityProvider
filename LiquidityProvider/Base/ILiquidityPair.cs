using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LiquidityProvider.LiquidityPairs
{
    internal interface ILiquidityPair
    {
        Task Initialize(Web3 web3, Account account, Configuration configuration);

        Task<bool> CheckChanged();
        Task<(bool success, string information)> CorrectDiapason();
    }
}
