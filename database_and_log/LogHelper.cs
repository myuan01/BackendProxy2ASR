using Serilog;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    public class LogHelper
    {
        public static string outputTemplate = "{Timestamp:dd-MM-yyyy HH:mm:ss} [{SourceContext}:{Level:u3}] {Message:lj}{NewLine}{Exception}";
        private static readonly LogHelper instance = new LogHelper();

        static LogHelper()
        {
        }

        private LogHelper()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(path: "config.json", optional: false, reloadOnChange: true)
                .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }

        public static LogHelper Instance
        {
            get
            {
                return instance;
            }
        }

        public ILogger GetLogger<T>()
        {
            return Log.ForContext<T>();
        }
    }

}
