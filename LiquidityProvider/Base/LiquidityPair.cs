using Nethereum.Contracts;
using Nethereum.DataServices.Etherscan;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LiquidityProvider.LiquidityPairs
{
    [Serializable]
    internal class LiquidityPair : ILiquidityPair
    {
        private const string ApiKey = "Q9RTI9WFQ13PWG9GGE6GXBYU89DXJSPPZ8";
        private readonly (int token, BigInteger value) _initializeBalance;
        private Web3? _web3;
        private Account? _account;
        private Contract? _contract;
        private (string address, string symbol) _tokenX;
        private (string address, string symbol) _tokenY;

        public string Name { get; set; } = string.Empty;
        public EtherscanChain Chain { get; set; }
        public string ContractAdress { get; set; }
        public string ContractProxyAdress { get; set; }
        public BigInteger CurrentActiveId { get; set; }
        public string LastDepositedToken { get; set; }

        public LiquidityPair(string contractAdress, EtherscanChain chain, (int token, BigInteger value) initializeBalance, string contractProxyAdress = "")
        {
            Chain = chain;
            ContractAdress = contractAdress;
            _initializeBalance = initializeBalance;
            ContractProxyAdress = string.IsNullOrEmpty(contractProxyAdress) ? contractAdress : contractProxyAdress;
            CurrentActiveId = 0;
            LastDepositedToken = string.Empty;

            _tokenX = (string.Empty, string.Empty);
            _tokenY = (string.Empty, string.Empty);
        }

        public virtual async Task Initialize(Web3 web3, Account account)
        {
            _web3 = web3;
            _account = account;

            var etherscanService = new EtherscanApiService(Chain, ApiKey);
            var abiContract = await etherscanService.Contracts.GetAbiAsync(ContractProxyAdress);
            _contract = _web3.Eth.GetContract(abiContract.Result, ContractAdress);

            var tokenAddressX = await _contract.GetFunction("getTokenX").CallAsync<string>();
            var tokenAddressY = await _contract.GetFunction("getTokenY").CallAsync<string>();
            var tokenSymbolX = await GetTokenSymbol(tokenAddressX);
            var tokenSymbolY = await GetTokenSymbol(tokenAddressY);

            _tokenX = (tokenAddressX, tokenSymbolX);
            _tokenY = (tokenAddressY, tokenSymbolY);
            Name = $"{tokenSymbolX}-{tokenSymbolY}";

            if (CurrentActiveId == 0)
                await InitializeLiquidity();
        }

        public virtual async Task<bool> CheckChanged()
        {
            if (_contract is null)
                return false;

            var activeId = await _contract.GetFunction("getActiveId").CallAsync<BigInteger>();
            return CurrentActiveId != activeId;
        }

        public virtual async Task<string> GetInforamtion()
        {
            if (_contract is null || CurrentActiveId == 0)
                return string.Empty;

            var binStep = await _contract.GetFunction("getBinStep").CallAsync<BigInteger>();
            var price = CalculatePrice(CurrentActiveId, binStep);
            return $"""
                Token pair: {Name}
                New active id - {CurrentActiveId} # {price:F8} # {DateTime.Now}
                """;
        }

        public async Task<bool> CorrectDiapason()
        {
            if (_contract is null || _web3 is null || _account is null)
                return false;

            var result = await LiquidityService.CorrectDiapason(_web3, _contract, ApiKey, Chain, CurrentActiveId, _account.Address, _tokenX, _tokenY);
            if(result.Item1)
                CurrentActiveId = result.Item2;

            return result.Item1;
        }

        private decimal CalculatePrice(BigInteger activeId, BigInteger binStep)
        {
            //price = (1 + binStep / 10_000) ^ (activeId - 2^23)

            var exponent = (int)(activeId - (BigInteger)Math.Pow(2, 23));
            if (exponent == 0)
                return 1m; // любое число в степени 0 равно 1

            decimal result = 1m;
            bool isNegativeExponent = exponent < 0;

            decimal baseNumber = (decimal)(1 + (int)binStep / 10000f);
            // Работаем с положительными степенями
            int positiveExponent = isNegativeExponent ? -exponent : exponent;
            for (int i = 0; i < positiveExponent; i++)
            {
                result *= baseNumber;
            }

            // Если степень была отрицательной, возвращаем обратное значение
            return isNegativeExponent ? 1m / result : result;
        }

        private async Task InitializeLiquidity()
        {
            if (_contract is null || _account is null || _web3 is null)
                return;

            var activeId = await _contract.GetFunction("getActiveId").CallAsync<BigInteger>();

            var amountX = _initializeBalance.token == 0 ? _initializeBalance.value : 0;
            var amountY = _initializeBalance.token != 0 ? _initializeBalance.value : 0;
            var result = await LiquidityService.AddLiquidity(_web3, _contract, ApiKey, Chain, _account.Address, _tokenX.address, _tokenY.address, amountX, amountY, activeId);
            if(result)
            {
                CurrentActiveId = activeId;
                Console.WriteLine("Success initilaize liquidity!");
            }
            else
                Console.WriteLine("Error initilaize liquidity!");
        }

        private async Task<string> GetTokenSymbol(string tokenAdress)
        {
            var contractService = _web3.Eth.ERC20.GetContractService(tokenAdress);
            return await contractService.SymbolQueryAsync();
        }
    }
}
