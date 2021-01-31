using System;
using database_and_log;
using Serilog;

namespace Fleck
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public class FleckLog
    {
        public static LogLevel Level = LogLevel.Info;
        private static ILogger _logger = new LogHelper<FleckLog>("../config.json").Logger;

        public static Action<LogLevel, string, Exception> LogAction = (level, message, ex) =>
        {
            if (level >= Level) {
                switch (level)
                {
                    case LogLevel.Warn:
                        _logger.Warning(message, ex);
                        break;

                    case LogLevel.Error:
                        _logger.Error(message, ex);
                        break;

                    case LogLevel.Debug:
                        _logger.Debug(message, ex);
                        break;

                    case LogLevel.Info:
                        _logger.Information(message, ex);
                        break;
                }
            }
        };

        public static void Warn(string message, Exception ex = null)
        {
            LogAction(LogLevel.Warn, message, ex);
        }

        public static void Error(string message, Exception ex = null)
        {
            LogAction(LogLevel.Error, message, ex);
        }

        public static void Debug(string message, Exception ex = null)
        {
            LogAction(LogLevel.Debug, message, ex);
        }

        public static void Info(string message, Exception ex = null)
        {
            LogAction(LogLevel.Info, message, ex);
        }

    }
}
