using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Contracts.ContractHandlers;
using Nethereum.Contracts.QueryHandlers.MultiCall;
using Nethereum.Contracts.Standards.ERC20.TokenList;
using Nethereum.DataServices.Etherscan;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3;
using Org.BouncyCastle.Crypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LiquidityProvider
{
    #region Add liquidity function
    [Struct("liquidityParameters")]
    public class LiquidityParameters
    {
        [Parameter("address", "tokenX", 1)]
        public string tokenX { get; set; }

        [Parameter("address", "tokenY", 2)]
        public string tokenY { get; set; }

        [Parameter("uint256", "binStep", 3)]
        public BigInteger binStep { get; set; }

        [Parameter("uint256", "amountX", 4)]
        public BigInteger amountX { get; set; }

        [Parameter("uint256", "amountY", 5)]
        public BigInteger amountY { get; set; }

        [Parameter("uint256", "amountXMin", 6)]
        public BigInteger amountXMin { get; set; }

        [Parameter("uint256", "amountYMin", 7)]
        public BigInteger amountYMin { get; set; }

        [Parameter("uint256", "activeIdDesired", 8)]
        public BigInteger activeIdDesired { get; set; }

        [Parameter("uint256", "idSlippage", 9)]
        public BigInteger idSlippage { get; set; }

        [Parameter("int256[]", "deltaIds", 10)]
        public List<BigInteger> deltaIds { get; set; }

        [Parameter("uint256[]", "distributionX", 11)]
        public List<BigInteger> distributionX { get; set; }

        [Parameter("uint256[]", "distributionY", 12)]
        public List<BigInteger> distributionY { get; set; }

        [Parameter("address", "to", 13)]
        public string to { get; set; }

        [Parameter("address", "refundTo", 14)]
        public string refundTo { get; set; }

        [Parameter("uint256", "deadline", 15)]
        public BigInteger deadline { get; set; }
    }

    [FunctionOutput]
    public class AddLiquidityOutput : IFunctionOutputDTO
    {
        [Parameter("uint256", "amountXAdded", 1)]
        public BigInteger amountXAdded { get; set; }
        [Parameter("uint256", "amountYAdded", 2)]
        public BigInteger amountYAdded { get; set; }
        [Parameter("uint256", "amountXLeft", 3)]
        public BigInteger amountXLeft { get; set; }
        [Parameter("uint256", "amountYLeft", 4)]
        public BigInteger amountYLeft { get; set; }
        [Parameter("uint256[]", "depositIds", 5)]
        public List<BigInteger> depositIds { get; set; }
        [Parameter("uint256[]", "liquidityMinted", 6)]
        public List<BigInteger> liquidityMinted { get; set; }

    }

    [Function("addLiquidityNATIVE", typeof(AddLiquidityOutput))]
    public class AddLiquidityFunction : FunctionMessage
    {
        [Parameter("tuple", "liquidityParameters")]
        public LiquidityParameters liquidityParameters { get; set; }
    }
    #endregion

    #region Remove liquidity function
    [FunctionOutput]
    public class GetBinOutput : IFunctionOutputDTO
    {
        [Parameter("uint128", "binReserveX", 1, false)]
        public BigInteger X { get; set; }
        [Parameter("uint128", "binReserveY", 2, false)]
        public BigInteger Y { get; set; }
    }

    [Function("removeLiquidityNATIVE", typeof(AddLiquidityOutput))]
    public class RemoveLiquidityFunction : FunctionMessage
    {
        [Parameter("address", "tokenX ", 1)]
        public string token { get; set; }

        [Parameter("uint16", "binStep", 2)]
        public ushort binStep { get; set; }

        [Parameter("uint256", "amountTokenMin", 3)]
        public BigInteger amountTokenMin { get; set; }

        [Parameter("uint256", "amountNATIVEMin", 4)]
        public BigInteger amountNATIVEMin { get; set; }

        [Parameter("uint256[]", "ids", 5)]
        public List<BigInteger> ids { get; set; }

        [Parameter("uint256[]", "amounts", 6)]
        public List<BigInteger> amounts { get; set; }

        [Parameter("address", "to", 7)]
        public string to { get; set; }

        [Parameter("uint256", "deadline", 8)]
        public BigInteger deadline { get; set; }
    }
    #endregion

    internal static class LiquidityService
    {
        public static async Task<bool> AddLiquidity(Web3 web3, Contract contract, string apiKey, EtherscanChain chain, string accountAddress, string tokenX, string tokenY, BigInteger amountX, BigInteger amountY, BigInteger activeId)
        {
            var binStep = await contract.GetFunction("getBinStep").CallAsync<ushort>();

            var deadline = new DateTimeOffset(DateTime.Now.AddDays(1)).ToUnixTimeSeconds();
            var liquidityParameters = new LiquidityParameters()
            {
                tokenX = tokenX,
                tokenY = tokenY,
                binStep = binStep,
                amountX = amountX,
                amountY = amountY,
                amountXMin = amountX == 0 ? 0 : amountX - (amountX / 100),
                amountYMin = amountY == 0 ? 0 : amountY - (amountY / 100),
                activeIdDesired = activeId,
                idSlippage = 0,
                deltaIds = new List<BigInteger>() { 0 },
                distributionX = new List<BigInteger>() { 1000000000000000000 },
                distributionY = new List<BigInteger>() { 1000000000000000000 },
                to = accountAddress,
                refundTo = accountAddress,
                deadline = deadline
            };

            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
            var function = new AddLiquidityFunction()
            {
                liquidityParameters = liquidityParameters,
                FromAddress = accountAddress,
            };

            var addLiquidityFunction = await GetLBProviderFunction<AddLiquidityFunction>(web3, apiKey, chain);

            var cancellationToken = new CancellationTokenSource().Token;
            var result = await addLiquidityFunction.SendTransactionAndWaitForReceiptAsync(function, accountAddress, gasPrice, amountY == 0 ? new HexBigInteger(0) : new HexBigInteger(amountY), cancellationToken);
            return result.Succeeded();
        }

        public static async Task<bool> RemoveLiquidity(Web3 web3, Contract contract, string apiKey, EtherscanChain chain, BigInteger currentActiveId, string accountAddress, string tokenX, BigInteger LBTokenAmount, BigInteger totalBalanceX, BigInteger totalBalanceY)
        {
            var binStep = await contract.GetFunction("getBinStep").CallAsync<ushort>();

            var deadline = new DateTimeOffset(DateTime.Now.AddDays(1)).ToUnixTimeSeconds();
            var function = new RemoveLiquidityFunction()
            {
                token = tokenX,
                binStep = binStep,
                amountTokenMin = totalBalanceX == 0 ? 0 : totalBalanceX - (totalBalanceX / 100),
                amountNATIVEMin = totalBalanceY == 0 ? 0 : totalBalanceY - (totalBalanceY / 100),
                ids = new List<BigInteger>() { currentActiveId },
                amounts = new List<BigInteger>() { LBTokenAmount },
                to = accountAddress,
                deadline = deadline
            };

            var removeLiquidityFunction = await GetLBProviderFunction<RemoveLiquidityFunction>(web3, apiKey, chain);

            var cancellationToken = new CancellationTokenSource().Token;
            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
            var result = await removeLiquidityFunction.SendTransactionAndWaitForReceiptAsync(function, accountAddress, gasPrice, new HexBigInteger(0), cancellationToken);
            return result.Succeeded();
        }

        public static async Task<(bool success, BigInteger activeId, string information)> CorrectDiapason(Web3 web3, Contract contract, string apiKey, EtherscanChain chain, BigInteger currentActiveId, string accountAddress, (string adress, string symbol) tokenX, (string adress, string symbol) tokenY)
        {
            // get mint id from current active id
            var LBTokenAmount = await contract.GetFunction("balanceOf").CallAsync<BigInteger>(new object[] { accountAddress, currentActiveId });
            var totalSupply = await contract.GetFunction("totalSupply").CallAsync<BigInteger>(new object[] { currentActiveId });
            var binReserve = await contract.GetFunction("getBin").CallAsync<GetBinOutput> (new object[] { (uint)currentActiveId });

            var totalBalanceX = LBTokenAmount * binReserve.X / totalSupply;
            var totalBalanceY = LBTokenAmount * binReserve.Y / totalSupply;

            var gasPrice = await web3.Eth.GasPrice.SendRequestAsync();
            if(gasPrice.Value > 15000000)
            {
                Console.WriteLine($"Error Gas too high {gasPrice.Value}");
                return (false, currentActiveId, string.Empty);
            }

            var result = await RemoveLiquidity(web3, contract, apiKey, chain, currentActiveId, accountAddress, tokenX.adress, LBTokenAmount, totalBalanceX, totalBalanceY);
            if (!result)
            {
                Console.WriteLine("Error RemoveLiquidity");
                return (false, currentActiveId, string.Empty);
            }

            var activeId = await contract.GetFunction("getActiveId").CallAsync<BigInteger>();

            result = await AddLiquidity(web3, contract, apiKey, chain, accountAddress, tokenX.adress, tokenY.adress, totalBalanceX, totalBalanceY, activeId);
            if (!result)
            {
                Console.WriteLine("Error AddLiquidity");
                return (false, currentActiveId, string.Empty);
            }

            var name = $"{tokenX.symbol}-{tokenY.symbol}";
            var balanceX = UnitConversion.Convert.FromWei(totalBalanceX, UnitConversion.EthUnit.Ether);
            var balanceY = UnitConversion.Convert.FromWei(totalBalanceY, UnitConversion.EthUnit.Ether);
            var information = $"""
                Correct diapason {name} # BalanceX {balanceX} # BalanceY {balanceY} # Id {activeId}
                """;

            return (true, activeId, information);
        }

        private static async Task<Nethereum.Contracts.Function<T>> GetLBProviderFunction<T>(Web3 web3, string apiKey, EtherscanChain chain)
        {
            var address = "0x18556DA13313f3532c54711497A8FedAC273220E";
            var etherscanService = new EtherscanApiService(chain, apiKey);
            var abiContract = await etherscanService.Contracts.GetAbiAsync(address);
            var contract = web3.Eth.GetContract(abiContract.Result, address);
            return contract.GetFunction<T>();
        }

        #region Decode
        private static async Task Decode(Web3 web3, string hash)
        {
            var txn = await web3.Eth.Transactions.GetTransactionByHash.SendRequestAsync(hash);
            //check if the transfer belongs to the Transfer Function if not ignore it
            if (txn.IsTransactionForFunctionMessage<AddLiquidityFunction>())
            {
                var transfer = new AddLiquidityFunction().DecodeTransaction(txn);
                int z = 1;
            }
        }

        [Event("TransferBatch")]
        public class TransferBatch : IEventDTO
        {
            [Parameter("address", "sender", 1, true)]
            public string sender { get; set; }

            [Parameter("address", "from", 2, true)]
            public string from { get; set; }

            [Parameter("address", "to", 2, true)]
            public string to { get; set; }

            [Parameter("uint256[]", "ids", 4)]
            public List<BigInteger> ids { get; set; }

            [Parameter("uint256[]", "amounts", 5)]
            public List<BigInteger> amounts { get; set; }
        }
        private static async Task DecodeEvent(Web3 web3, string apiKey, EtherscanChain chain, TransactionReceipt transactionReceipt)
        {
            var address = "0x7a5b4e301fc2B148ceFe57257A236EB845082797";
            var etherscanService = new EtherscanApiService(chain, apiKey);
            var abiContract = await etherscanService.Contracts.GetAbiAsync(address);
            var contract = web3.Eth.GetContract(abiContract.Result, address);

            var eventTransferBatch = contract.GetEvent<TransferBatch>();
            var eventOutputs = eventTransferBatch.DecodeAllEventsForEvent(transactionReceipt.Logs);
        }
        #endregion
    }
}
