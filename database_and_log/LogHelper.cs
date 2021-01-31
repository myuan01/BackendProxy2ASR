using Serilog;
using System.IO;
using System;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    public class LogHelper<T>
    {
        public static string outputTemplate = "{Timestamp:dd-MM-yyyy HH:mm:ss} [{SourceContext}:{Level:u3}] {Message:lj}{NewLine}{Exception}";

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
