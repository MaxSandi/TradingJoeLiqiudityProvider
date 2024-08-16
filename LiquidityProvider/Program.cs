﻿// See https://aka.ms/new-console-template for more information
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Nethereum.DataServices.Etherscan;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using LiquidityProvider.LiquidityPairs;
using Newtonsoft.Json;
using Nethereum.Util;
using Microsoft.VisualBasic;
using LiquidityProvider.Properties;
using LiquidityProvider;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        TelegramBotClient botClient = new TelegramBotClient(Resources.TELEGRAM_API);
        using CancellationTokenSource cts = new();
        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            cancellationToken: cts.Token
        );

        string configurationFilePath = Path.Combine(AppContext.BaseDirectory, "configuration.json");
        var configuration = Deserialize<Configuration>(configurationFilePath);
        if (configuration is null)
        {
            Console.WriteLine("Can't load configuration.json");
            Console.ReadLine();
            return;
        }

        string pairsFilePath = Path.Combine(AppContext.BaseDirectory, "pairs.json");
        var liquidityPairs = Deserialize<List<LiquidityPair>>(pairsFilePath);
        if(liquidityPairs is null)
        {
            Console.WriteLine("Can't load pairs.json");
            Console.ReadLine();
            return;
        }

        #region Initialize
        //{
        //    liquidityPairs = new List<LiquidityPair>();
        //    liquidityPairs.Add(new LiquidityPair("0x6816b2A43374B5ad8d0FfBdfaa416144ff5aCa3A", EtherscanChain.Arbitrum, (0, UnitConversion.Convert.ToWei(1.146230284517827734, UnitConversion.EthUnit.Ether)), "0x7a5b4e301fc2B148ceFe57257A236EB845082797"));
        //}
        //var liquidityPairs = new List<ILiquidityPair>()
        //{
        //    new LiquidityPair("WETH-ETH", EtherscanChain.Arbitrum, "0x2088eB5E23F24458e241430eF155d4EC05BBc9e8", "0x7a5b4e301fc2B148ceFe57257A236EB845082797"),
        //    //new LiquidityPair(web3, "JOE-ETH", EtherscanChain.Arbitrum, "0xb147468ab3CD4bBb66c48628C00D6a49be0e9ec3", "0x7a5b4e301fc2B148ceFe57257A236EB845082797"),
        //    new LiquidityPair("USDV-USDC", EtherscanChain.Arbitrum, "0xE28b5df4C2da6145a3d9b8FF076231ABb534B103", "0x7a5b4e301fc2B148ceFe57257A236EB845082797"),
        //    new LiquidityPair("WAVAX-ETH", EtherscanChain.Arbitrum, "0x6816b2A43374B5ad8d0FfBdfaa416144ff5aCa3A", "0x7a5b4e301fc2B148ceFe57257A236EB845082797"),
        //};
        #endregion

        var account = new Account(configuration.AccountKey);
        var web3 = new Web3(account, "https://arbitrum-mainnet.infura.io/v3/7be99096d466482789b45c682edf456d");

        foreach (var item in liquidityPairs)
            await item.Initialize(web3, account);

        Console.WriteLine($"Inititialized {liquidityPairs.Count} pairs!");

        var monitoringTask = MonitoringLiquidityAsync(liquidityPairs);

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
            long notifyUserId = long.Parse(Resources.NotifyUserId);
            if (notifyUserId != 0)
                await botClient.SendTextMessageAsync(notifyUserId, information);
        }
        #endregion

        async Task MonitoringLiquidityAsync(List<LiquidityPair> liquidityPairs)
        {
            Console.WriteLine($"Start monitoring");

            while (true)
            {
                try
                {
                    foreach (var liquidityPair in liquidityPairs)
                    {
                        var result = await liquidityPair.CheckChanged();
                        if(result)
                        {
                            if(await liquidityPair.CorrectDiapason())
                            {
                                var information = await liquidityPair.GetInforamtion();
                                Console.WriteLine(information);

                                await NotifyTelegramAsync(information);

                                Serialize(liquidityPairs, pairsFilePath);
                            }
                        }

                        await Task.Delay(100);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{e.Message} # {DateTime.Now}");
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
    }
}

#region Telegram api

#endregion
