using Nethereum.Contracts;
using Nethereum.Contracts.QueryHandlers.MultiCall;
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
        private readonly (int token, BigInteger value) _initializeBalance;
        private Web3? _web3;
        private Account? _account;
        private Configuration? _configuration;
        private EtherscanApiService? _etherscanService;
        private Contract? _contract;
        private (string address, string symbol) _tokenX;
        private (string address, string symbol) _tokenY;

        public string Name => $"{_tokenX.symbol}-{_tokenY.symbol}";

        public EtherscanChain Chain { get; set; }
        public string ContractAdress { get; set; }
        public string ContractProxyAdress { get; set; }
        public BigInteger CurrentActiveId { get; set; }
        public bool OnlyMonitoring { get; set; } = false;

        public LiquidityPair(string contractAdress, EtherscanChain chain, (int token, BigInteger value) initializeBalance, string contractProxyAdress = "")
        {
            Chain = chain;
            ContractAdress = contractAdress;
            _initializeBalance = initializeBalance;
            ContractProxyAdress = string.IsNullOrEmpty(contractProxyAdress) ? contractAdress : contractProxyAdress;
            CurrentActiveId = 0;

            _tokenX = (string.Empty, string.Empty);
            _tokenY = (string.Empty, string.Empty);
        }

        public virtual async Task Initialize(Web3 web3, Account account, Configuration configuration)
        {
            _web3 = web3;
            _account = account;
            _configuration = configuration;
            _etherscanService = new EtherscanApiService(Chain, _configuration.EtherscanAPIKey);

            var abiContract = await _etherscanService.Contracts.GetAbiAsync(ContractProxyAdress);
            _contract = _web3.Eth.GetContract(abiContract.Result, ContractAdress);

            var tokenAddressX = await _contract.GetFunction("getTokenX").CallAsync<string>();
            var tokenAddressY = await _contract.GetFunction("getTokenY").CallAsync<string>();
            var tokenSymbolX = await GetTokenSymbol(tokenAddressX);
            var tokenSymbolY = await GetTokenSymbol(tokenAddressY);

            _tokenX = (tokenAddressX, tokenSymbolX);
            _tokenY = (tokenAddressY, tokenSymbolY);

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

        public async Task<(bool success, string information)> CorrectDiapason()
        {
            if (_contract is null || _web3 is null || _account is null || _configuration is null || _etherscanService is null)
                return (false, "Not initialize!");

            if (OnlyMonitoring)
            {
                var binStep = await _contract.GetFunction("getBinStep").CallAsync<BigInteger>();
                var activeId = await _contract.GetFunction("getActiveId").CallAsync<BigInteger>();
                var price = CalculatePrice(CurrentActiveId, binStep);
                var information = $"""
                Token pair: {Name}
                New active id - {CurrentActiveId} # {price:F8} # {DateTime.Now}
                """;
                Console.WriteLine(information);

                CurrentActiveId = activeId;
                return (true, string.Empty);
            }
            else
            {
                var result = await LiquidityService.CorrectDiapason(_web3, _contract, _configuration, _etherscanService, CurrentActiveId, _account.Address, _tokenX, _tokenY);
                if (result.success)
                    CurrentActiveId = result.activeId;

                return (result.success, result.information);
            }
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
            if (_contract is null || _account is null || _web3 is null || _etherscanService is null)
                return;

            var activeId = await _contract.GetFunction("getActiveId").CallAsync<BigInteger>();

            if (_initializeBalance.value == 0 || OnlyMonitoring)
            {
                CurrentActiveId = activeId;
                Console.WriteLine($"Token pair: {Name} Success initilaize liquidity!");
                return;
            }

            var amountX = _initializeBalance.token == 0 ? _initializeBalance.value : 0;
            var amountY = _initializeBalance.token != 0 ? _initializeBalance.value : 0;
            var result = await LiquidityService.AddLiquidity(_web3, _contract, _etherscanService, _account.Address, _tokenX.address, _tokenY.address, amountX, amountY, activeId);
            if(result)
            {
                CurrentActiveId = activeId;
                Console.WriteLine($"Token pair: {Name} Success initilaize liquidity!");
            }
            else
                Console.WriteLine($"Token pair: {Name} Error initilaize liquidity!");
        }

        private async Task<string> GetTokenSymbol(string tokenAdress)
        {
            var contractService = _web3.Eth.ERC20.GetContractService(tokenAdress);
            return await contractService.SymbolQueryAsync();
        }
    }
}
