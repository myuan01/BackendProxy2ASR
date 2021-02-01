using System;
using System.IO;
using System.Linq;
using Serilog;
using database_and_log;
using Microsoft.Extensions.Configuration;


namespace BackendProxy2ASR
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Invalid arguments. Please pass in path to config.json file.");
                return;
            }

            string configPath = args[0];
            configPath = Path.GetFullPath(configPath, Directory.GetCurrentDirectory());
            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file at {configPath} does not exists. Please pass in a valid path.");
                return;
            }

            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile(path: configPath, optional: false, reloadOnChange: true)
                .Build();

            // Init LogHelper
            LogHelper.Initialize(config);
            ILogger logger = LogHelper.GetLogger<Program>();

            ProxyASR proxy = new ProxyASR(config);
            proxy.Start();

            var input = Console.ReadLine();
            logger.Information(input);
            while (input != "exit")
            {
                foreach (var socket in proxy.allSockets.ToList())
                {
                    socket.Send(input);
                }
                input = Console.ReadLine();
            }
        }
    }
}
