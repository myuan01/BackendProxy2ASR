using System;
using Npgsql;
using Serilog;
using Microsoft.Extensions.Configuration;
using System.Data;

#nullable enable


namespace database_and_log
{
    public class DatabaseHelper
    {
        private NpgsqlConnection? _conn;
        private ILogger _logger = LogHelper.GetLogger<DatabaseHelper>();
        private readonly bool _toStoreAudio = false;

        public DatabaseHelper(IConfiguration config)
        {
            // read config from configFilePath and parse into connection string
            // connectionString = "Host=localhost;Username=postgres;Password=password;Database=ai_3_staging";
            bool toConnect = ConfigurationBinder.GetValue<bool>(config.GetSection("Database"), "ToConnect");
            _toStoreAudio = ConfigurationBinder.GetValue<bool>(config.GetSection("Database"), "StoreAudio");

            if (toConnect)
            {
                string connectionString = LoadAndParseConfig(config);
                // Connect to db
                _logger.Information($"Creating db connection...");
                _conn = new NpgsqlConnection(connectionString);
            }
            else
            {
                _logger.Information($"toConnect in config is {toConnect}. Not creating a db connection...");
            }
        }

        public bool IsConnected()
        {
            if (_conn != null)
            {
                return _conn.State == ConnectionState.Open;
            }

            return false;
        }

        private string LoadAndParseConfig(IConfiguration config)
        {
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
                if (_conn != null)
                {
                    _logger.Information("Opening database connection...");
                    _conn.Open();
                    return true;
                }
                return false;
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
                if (_conn != null)
                {
                    _logger.Information("Closing database connection...");
                    _conn.Close();
                    return true;
                }
                return false;
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
            string input_word,
            string return_text
        )
        {
            if (_conn != null)
            {
                string sqlStatement = @"CALL public.insert_audio_stream_prediction(
                @user_id, @device_id, @session_id, @seq_id, @app_id, 
                @pred_timestamp, @input_word, @return_text)";

                using var cmd = new NpgsqlCommand(sqlStatement, _conn);
                cmd.Parameters.AddWithValue("user_id", user_id);
                cmd.Parameters.AddWithValue("device_id", device_id);
                cmd.Parameters.AddWithValue("session_id", session_id);
                cmd.Parameters.AddWithValue("seq_id", seq_id);
                cmd.Parameters.AddWithValue("app_id", app_id);
                cmd.Parameters.AddWithValue("pred_timestamp", pred_timestamp);
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
            return false;
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
            if (_conn != null)
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

            return false;
        }


        public bool UpdateAudioStreamPrediction(
            string session_id,
            int seq_id,
            int? user_id = null,
            string? device_id = null,
            int? app_id = null,
            DateTime? pred_timestamp = null,
            string? input_word = null,
            string? return_text = null
        )
        {
            if (_conn != null)
            {
                string sqlStatement = @"CALL public.update_audio_stream_prediction(
                @session_id, @seq_id, @user_id, @device_id, @app_id, 
                @pred_timestamp, @input_word, @return_text)";

                using var cmd = new NpgsqlCommand(sqlStatement, _conn);
                cmd.Parameters.AddWithValue("session_id", session_id);
                cmd.Parameters.AddWithValue("seq_id", seq_id);
                cmd.Parameters.AddWithValue("user_id", user_id ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("device_id", device_id ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("app_id", app_id ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("pred_timestamp", pred_timestamp ?? (object)DBNull.Value);
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

            return false;
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
            if (_conn != null)
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

            return false;
        }


        public bool Is_User(string username, string password)
        {
            string sqlStatement = @"SELECT public.is_user(@username, @password)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("username", username);
            cmd.Parameters.AddWithValue("password", password);

            try
            {
                _logger.Information($"Querying database for user {username}...");
                object? result = cmd.ExecuteScalar();

                if (result != null)
                {
                    bool is_user = (bool)result;
                    if (is_user)
                    {
                        _logger.Information($"user {username} authenticated.");
                    }
                    else
                    {
                        _logger.Error($"Invalid password for user {username}!!!");
                    }

                    return is_user;
                }
                else
                {
                    _logger.Error($"Empty result from querying user {username}");
                    return false;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }

        public int insert_playback(byte[] message, string session_id)
        {
            if (_conn != null && _toStoreAudio)
            {
                string sqlStatement = @"CALL public.insert_playback(@message, @session_id)";

                using var cmd = new NpgsqlCommand(sqlStatement, _conn);
                cmd.Parameters.AddWithValue("session_id", session_id);
                cmd.Parameters.AddWithValue("message", message);

                try
                {
                    _logger.Information($"Insert playback for session {session_id}.");
                    return Convert.ToInt32(cmd.ExecuteScalar());
                }
                catch (Exception e)
                {
                    _logger.Error(e, e.Message);
                    return -1;
                }
            }

            return -1;
        }

        public bool update_playback(int playback_id, double delay)
        {
            string sqlStatement = @"CALL public.update_playback(@playback_id, @delay)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("playback_id", playback_id);
            cmd.Parameters.AddWithValue("delay", delay);

            try
            {
                cmd.ExecuteNonQuery();
                _logger.Information($"Updating playback for playback_id {playback_id}.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                return false;
            }
        }

        public NpgsqlDataReader GetPlayback(string session_id)
        {
            string sqlStatement = @"SELECT * from public.get_playback(@session_id)";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);
            cmd.Parameters.AddWithValue("session_id", session_id);

            // Execute the query and obtain a result set
            NpgsqlDataReader dr = cmd.ExecuteReader();

            return dr;
        }

        public NpgsqlDataReader GetPlayback()
        {
            string sqlStatement = @"SELECT * from public.get_playback()";

            using var cmd = new NpgsqlCommand(sqlStatement, _conn);

            // Execute the query and obtain a result set
            NpgsqlDataReader dr = cmd.ExecuteReader();

            return dr;
        }
    }
}
