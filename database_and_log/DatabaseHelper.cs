using System;
using System.IO;
using Npgsql;
using Serilog;
using Microsoft.Extensions.Configuration;

#nullable enable


namespace database_and_log
{
    public class DatabaseHelper
    {
        private NpgsqlConnection _conn;
        private ILogger _logger;

        public DatabaseHelper(string jsonConfigFilePath)
        {
            // read config from configFilePath and parse into connection string
            // connectionString = "Host=localhost;Username=postgres;Password=password;Database=ai_3_staging";
            string connectionString = LoadAndParseConfig(jsonConfigFilePath: jsonConfigFilePath);

            // Connect to db
            _conn = new NpgsqlConnection(connectionString);

            // setup logger
            _logger = new LogHelper<DatabaseHelper>("../config.json").Logger;
        }

        private string LoadAndParseConfig(string jsonConfigFilePath)
        {
            jsonConfigFilePath = Path.GetFullPath(jsonConfigFilePath, Directory.GetCurrentDirectory());
            var config = new ConfigurationBuilder()
                .AddJsonFile(path: jsonConfigFilePath, optional: false, reloadOnChange: true)
                .Build();

            string host = config.GetSection("Database")["Host"];
            string username = config.GetSection("Database")["Username"];
            string password = config.GetSection("Database")["Password"];
            string database = config.GetSection("Database")["Database"];

            string connectionString = $@"Host={host};Username={username};Password={password};Database={database}";
            return connectionString;
        }

        public bool Open()
        {
            try
            {
                _logger.Information("Opening database connection...");
                _conn.Open();
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool Close()
        {
            try
            {
                _logger.Information("Closing database connection...");
                _conn.Close();
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool InsertAudioStreamPrediction(
            int user_id,
            string device_id,
            string session_id,
            int seq_id,
            int app_id,
            DateTime pred_timestamp,
            byte[] input_audio,
            string input_word,
            string return_text
        )
        {
            string sqlStatement = @"CALL public.insert_audio_stream_prediction(
                @user_id, @device_id, @session_id, @seq_id, @app_id, 
                @pred_timestamp, @input_audio, @input_word, @return_text)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("user_id", user_id);
            cmd.Parameters.AddWithValue("device_id", device_id);
            cmd.Parameters.AddWithValue("session_id", session_id);
            cmd.Parameters.AddWithValue("seq_id", seq_id);
            cmd.Parameters.AddWithValue("app_id", app_id);
            cmd.Parameters.AddWithValue("pred_timestamp", pred_timestamp);
            cmd.Parameters.AddWithValue("input_audio", input_audio);
            cmd.Parameters.AddWithValue("input_word", input_word);
            cmd.Parameters.AddWithValue("return_text", return_text);

            try
            {
                _logger.Information($"Storing prediction for session {session_id}, seq {seq_id}.");
                cmd.ExecuteNonQuery();
                _logger.Information("Database insertion complete.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool InsertAudioStreamInfo(
            int user_id,
            string device_id,
            string session_id,
            int seq_id,
            int app_id,
            DateTime proc_start_time,
            DateTime proc_end_time,
            long stream_duration
        )
        {
            string sqlStatement = @"CALL public.insert_audio_stream_info(
                @user_id, @device_id, @session_id, @seq_id, @app_id, 
                @proc_start_time, @proc_end_time, @stream_duration)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("user_id", user_id);
            cmd.Parameters.AddWithValue("device_id", device_id);
            cmd.Parameters.AddWithValue("session_id", session_id);
            cmd.Parameters.AddWithValue("seq_id", seq_id);
            cmd.Parameters.AddWithValue("app_id", app_id);
            cmd.Parameters.AddWithValue("proc_start_time", proc_start_time);
            cmd.Parameters.AddWithValue("proc_end_time", proc_end_time);
            cmd.Parameters.AddWithValue("stream_duration", stream_duration);
            try
            {
                _logger.Information($"Storing stream info for session {session_id}, seq {seq_id}.");
                cmd.ExecuteNonQuery();
                _logger.Information("Database insertion complete.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool UpdateAudioStreamPrediction(
            string session_id,
            int seq_id,
            int? user_id = null,
            string? device_id = null,
            int? app_id = null,
            DateTime? pred_timestamp = null,
            byte[]? input_audio = null,
            string? input_word = null,
            string? return_text = null
        )
        {
            string sqlStatement = @"CALL public.update_audio_stream_prediction(
                @session_id, @seq_id, @user_id, @device_id, @app_id, 
                @pred_timestamp, @input_audio, @input_word, @return_text)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("session_id", session_id);
            cmd.Parameters.AddWithValue("seq_id", seq_id);
            cmd.Parameters.AddWithValue("user_id", user_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("device_id", device_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("app_id", app_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("pred_timestamp", pred_timestamp ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("input_audio", input_audio ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("input_word", input_word ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("return_text", return_text ?? (object)DBNull.Value);

            try
            {
                _logger.Information($"Updating prediction for session {session_id}, seq {seq_id}.");
                cmd.ExecuteNonQuery();
                _logger.Information("Database update complete.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool UpdateAudioStreamInfo(
            string session_id,
            int seq_id,
            int? user_id = null,
            string? device_id = null,
            int? app_id = null,
            DateTime? proc_start_time = null,
            DateTime? proc_end_time = null,
            long? stream_duration = null
        )
        {
            string sqlStatement = @"CALL public.update_audio_stream_info(
                @session_id, @seq_id, @user_id, @device_id, @app_id, 
                @proc_start_time, @proc_end_time, @stream_duration)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("session_id", session_id);
            cmd.Parameters.AddWithValue("seq_id", seq_id);
            cmd.Parameters.AddWithValue("user_id", user_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("device_id", device_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("app_id", app_id ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("proc_start_time", proc_start_time ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("proc_end_time", proc_end_time ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("stream_duration", stream_duration ?? (object)DBNull.Value);

            try
            {
                _logger.Information($"Updating stream info for session {session_id}, seq {seq_id}.");
                cmd.ExecuteNonQuery();
                _logger.Information("Database update complete.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }


        public bool ReadAudioStream(string session_id, int seq_id, string outputPath)
        {
            string sqlStatement = @"SELECT input_audio
                FROM public.asr_audio_stream_prediction
                WHERE session_id=@session_id AND seq_id=@seq_id;";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("session_id", session_id);
            cmd.Parameters.AddWithValue("seq_id", seq_id);

            try
            {
                _logger.Information($"Reading audio info for session {session_id}, seq {seq_id}.");
                // Execute the query and obtain a result set
                NpgsqlDataReader reader = cmd.ExecuteReader();
                reader.Read();
                byte[] audioFile = (byte[])reader[0];

                _logger.Information($"Writing audio file to {outputPath}");
                File.WriteAllBytes(outputPath, audioFile);
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }
    }

}

