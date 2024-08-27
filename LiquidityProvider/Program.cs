// See https://aka.ms/new-console-template for more information
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using LiquidityProvider.LiquidityPairs;
using Newtonsoft.Json;
using LiquidityProvider;
using Nethereum.Signer;
using Nethereum.DataServices.Etherscan;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        string configurationFilePath = Path.Combine(AppContext.BaseDirectory, "configuration.json");
        var configuration = Deserialize<Configuration>(configurationFilePath);
        if (configuration is null)
        {
            Console.WriteLine("ERROR: Can't load configuration.json");
            Console.ReadLine();
            return;
        }

        Console.WriteLine("Insert wallet private key:");
        configuration.AccountKey = Console.ReadLine();

        ClearCurrentConsoleLine();
        Console.WriteLine("Wallet private key was initialized!");

        var result = CheckConfiguration(configuration);
        if (!result.Item1)
        {
            Console.WriteLine(result.Item2);
            Console.ReadLine();
            return;
        }

        TelegramBotClient botClient = new TelegramBotClient(configuration.TelegramAPI);
        using CancellationTokenSource cts = new();
        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            cancellationToken: cts.Token
        );

        #region Initialize
        string pairsFilePath = Path.Combine(AppContext.BaseDirectory, "pairs.json");
        //var liquidityPairs = new List<LiquidityPair>()
        //{
        //    new LiquidityPair("0x6816b2A43374B5ad8d0FfBdfaa416144ff5aCa3A", EtherscanChain.Arbitrum, (0, 0), "0x7a5b4e301fc2B148ceFe57257A236EB845082797") { OnlyMonitoring = true },
        //    new LiquidityPair("0x2088eB5E23F24458e241430eF155d4EC05BBc9e8", EtherscanChain.Arbitrum, (0, 0), "0x7a5b4e301fc2B148ceFe57257A236EB845082797") { OnlyMonitoring = true },
        //    new LiquidityPair("0xE28b5df4C2da6145a3d9b8FF076231ABb534B103", EtherscanChain.Arbitrum, (0, 0), "0x7a5b4e301fc2B148ceFe57257A236EB845082797") { OnlyMonitoring = true },
        //    new LiquidityPair("0xe27C3153EA4479F6BE5D3c909c8Dc4807c86FddA", EtherscanChain.Arbitrum, (0, 0), "0x7a5b4e301fc2B148ceFe57257A236EB845082797") { OnlyMonitoring = true },
        //    new LiquidityPair("0xC09F4ad33a164e29DF3c94719ffD5F7B5B057781", EtherscanChain.Arbitrum, (0, 0), "0x7a5b4e301fc2B148ceFe57257A236EB845082797") { OnlyMonitoring = true },
        //};
        var liquidityPairs = Deserialize<List<LiquidityPair>>(pairsFilePath);
        if (liquidityPairs is null)
        {
            Console.WriteLine("Can't load pairs.json");
            Console.ReadLine();
            return;
        }
        #endregion

        var account = new Account(configuration.AccountKey);
        var web3 = new Web3(account, configuration.RpcEndpoint);
        var abiContractService = new AbiContractService(EtherscanChain.Arbitrum, configuration.EtherscanAPIKey, configuration.AbiContracts);

        foreach (var item in liquidityPairs)
            await item.Initialize(web3, account, configuration, abiContractService);

        Console.WriteLine($"Inititialized {liquidityPairs.Count} pairs!");
        Serialize(liquidityPairs, pairsFilePath);

        var monitoringTask = MonitoringLiquidityAsync(liquidityPairs);

        configuration.AbiContracts = abiContractService.AbiContracts;
        Serialize(configuration, configurationFilePath);

        Console.WriteLine($"Press any button to exit!");
        Console.ReadLine();

        #region Telegram api
        async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken cancellationToken)
        {
            try
            {
                if (update.Type == UpdateType.Message && update.Message!.Type == MessageType.Text && update.Message.Text == "/start")
                {
                    var chatId = update.Message.Chat.Id;
                    Console.WriteLine(chatId.ToString());

                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Registered!",
                        cancellationToken: cancellationToken
                    );
                }
            }
            catch (Exception exception)
            {
                var ErrorMessage = exception switch
                {
                    ApiRequestException apiRequestException
                        => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                    _ => exception.ToString()
                };

                Console.WriteLine(ErrorMessage);
            }
        }

        Task HandlePollingErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        async Task NotifyTelegramAsync(string information)
        {
            if (configuration.NotifyUserID != 0)
                await botClient.SendTextMessageAsync(configuration.NotifyUserID, information);
        }
        #endregion

        (bool, string) CheckConfiguration(Configuration configuration)
        {
            if (string.IsNullOrEmpty(configuration.TelegramAPI))
                return (false, "ERROR: Configuration TelegramAPI is empty");
            if (string.IsNullOrEmpty(configuration.AccountKey))
                return (false, "ERROR: Configuration AccountKey is empty");
            if (string.IsNullOrEmpty(configuration.RpcEndpoint))
                return (false, "ERROR: Configuration RpcEndpoint is empty");

            return (true, string.Empty);
        }

        async Task MonitoringLiquidityAsync(List<LiquidityPair> liquidityPairs)
        {
            Console.WriteLine($"Start monitoring");

            while (true)
            {
                try
                {
                    foreach (var liquidityPair in liquidityPairs)
                    {
                        var isChanged = await liquidityPair.CheckChanged();
                        if(isChanged)
                        {
                            var result = await liquidityPair.CorrectDiapason();
                            if (result.success)
                            {
                                if(!string.IsNullOrEmpty(result.information))
                                {
                                    Console.WriteLine(result.information);
                                    await NotifyTelegramAsync(result.information);
                                }

                                Serialize(liquidityPairs, pairsFilePath);
                            }
                        }

                        await Task.Delay(100);
                    }
                }
                catch (Exception e)
                {
                    var errorMessage = $"{e.Message} # {DateTime.Now}";
                    Console.WriteLine(errorMessage);
                    await NotifyTelegramAsync(errorMessage);
                }


                // Ждем 1 минуту
                await Task.Delay(60000);
            }
        }

        #region Serialization
        void Serialize<T>(T liquidityPairs, string filePath)
        {
            var data = JsonConvert.SerializeObject(liquidityPairs, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
            System.IO.File.WriteAllText(filePath, data);
        }

        T? Deserialize<T>(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                string json = System.IO.File.ReadAllText(filePath);
                var topicDatas = JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });
                if (topicDatas is null)
                    return default(T);

                return topicDatas;
            }

            return default(T);
        }
        #endregion

        void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop - 1; // Строка выше текущей (т.к. ReadLine добавляет новую строку)
            Console.SetCursorPosition(0, currentLineCursor); // Установка курсора в начало строки
            Console.Write(new string(' ', Console.WindowWidth)); // Перезаписываем строку пробелами
            Console.SetCursorPosition(0, currentLineCursor); // Возвращаем курсор в начало строки
        }
    }
}

#region Telegram api

#endregion
