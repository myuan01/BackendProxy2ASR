using Serilog;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    public static class LogHelper
    {
        private static IConfiguration _config;

        public static void Initialize(IConfiguration config)
        {
            _config = config;

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(config)
                .CreateLogger();
        }


        public static ILogger GetLogger<T>()
        {
            if (_config == null)
            {
                throw new System.Exception("LogHelper not initialized.");
            }
            return Log.ForContext<T>();
        }

    }

}
