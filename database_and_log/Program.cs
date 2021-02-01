using System;
using System.IO;
using Serilog;
using Microsoft.Extensions.Configuration;

namespace database_and_log
{
    class Program
    {
        private const string usageText = "Usage: demo configPath";
        private const int test_user_id = 1;
        private const string test_device_id = "device_id_1";

        private const string test_session_id = "session_id_3";
        private const int test_seq_id = 10;
        private const int test_app_id = 1;

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine(usageText);
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
            ILogger log = LogHelper.GetLogger<Program>();

            // Create dbhelper
            DatabaseHelper databaseHelper = new DatabaseHelper(config);
            bool connectionResult = databaseHelper.Open();
            log.Information($"Opening connection success? : {connectionResult}");

            if (connectionResult)
            {
                DateTime startTime = DateTime.UtcNow;

                Random rnd = new Random();
                TimeSpan timeSpan = new TimeSpan(0, 0, rnd.Next(1, 60));    // mimic time taken for prediction operation
                DateTime endTime = startTime + timeSpan;

                // insert into asr_audio_stream_prediction
                databaseHelper.InsertAudioStreamPrediction(user_id: test_user_id,
                    device_id: test_device_id,
                    session_id: test_session_id,
                    seq_id: test_seq_id,
                    app_id: test_app_id,
                    pred_timestamp: endTime,
                    input_word: "test_input_word",
                    return_text: "test_return_text");

                databaseHelper.InsertAudioStreamInfo(user_id: test_user_id,
                    device_id: test_device_id,
                    session_id: test_session_id,
                    seq_id: test_seq_id,
                    app_id: test_app_id,
                    proc_start_time: startTime,
                    proc_end_time: endTime,
                    stream_duration: (long)(endTime - startTime).TotalMilliseconds);
            }

            /* Demo code for updating AudioStreamPrediction
            bool result = Database.UpdateAudioStreamPrediction(user_id: 1,
                session_id: "session_id_1",
                seq_id: 5,
                return_text: "test c# client number 2",
                pred_timestamp: DateTime.UtcNow);

            Console.WriteLine(result);
            */

            connectionResult = databaseHelper.Close();
            log.Information($"Closing connection success? : {connectionResult}");
        }
    }
}
