using Serilog;
using System.IO;
using System;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    public class LogHelper
    {
        public static string outputTemplate = "{Timestamp:dd-MM-yyyy HH:mm:ss} [{SourceContext}:{Level:u3}] {Message:lj}{NewLine}{Exception}";
        private static readonly LogHelper instance = new LogHelper();
        private static string _jsonConfigFilePath = "";

        public static void InitLogHelper(string jsonConfigFilePath)
        {
            jsonConfigFilePath = Path.GetFullPath(jsonConfigFilePath, Directory.GetCurrentDirectory());
            _jsonConfigFilePath = jsonConfigFilePath;
            var config = new ConfigurationBuilder()
                .AddJsonFile(path: jsonConfigFilePath, optional: false, reloadOnChange: true)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }

        public static ILogger GetLogger<T>()
        {
            if (_jsonConfigFilePath == "")
            {
                throw new Exception("LogHelper not initialized. Please call the InitLogHelper method.");
            }
            return Log.ForContext<T>();
        }
    }

}
