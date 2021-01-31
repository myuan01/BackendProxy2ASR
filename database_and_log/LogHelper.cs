using Serilog;
using System.IO;
using System;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    public class LogHelper<T>
    {
        public LogHelper(string jsonConfigFilePath)
        {
            jsonConfigFilePath = Path.GetFullPath(jsonConfigFilePath, Directory.GetCurrentDirectory());
            var config = new ConfigurationBuilder()
                .AddJsonFile(path: jsonConfigFilePath, optional: false, reloadOnChange: true)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();

            Logger = Log.ForContext<T>();
        }

        public ILogger Logger { get; }

    }

}
