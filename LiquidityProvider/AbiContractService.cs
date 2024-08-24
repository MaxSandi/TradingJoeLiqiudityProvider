using Nethereum.DataServices.Etherscan;
using Nethereum.Signer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiquidityProvider
{
    internal class AbiContractService
    {
        private readonly EtherscanApiService _etherscanService;
        private readonly Dictionary<string, string> _storedAbiContracts;

        public Dictionary<string, string> AbiContracts => _storedAbiContracts;

        public AbiContractService(EtherscanChain chain, string apiKey, Dictionary<string, string> storedAbiContracts)
        {
            _etherscanService = new EtherscanApiService(chain, apiKey);

            _storedAbiContracts = storedAbiContracts;
        }

        public async Task<string?> GetAbiAsync(string contractAddress)
        {
            if(!_storedAbiContracts.TryGetValue(contractAddress, out var abi))
            {
                var response = await _etherscanService.Contracts.GetAbiAsync(contractAddress);
                if(response is not null && response.Status == "1")
                {
                    abi = response.Result;
                    _storedAbiContracts.Add(contractAddress, abi);
                }
            }

            return abi;
        }
    }
}
