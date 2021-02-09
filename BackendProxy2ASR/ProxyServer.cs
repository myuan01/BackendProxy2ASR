using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Fleck;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

using Serilog;
using database_and_log;


namespace BackendProxy2ASR
{
    class AnswerPlusSessionID
    {
        public string right_text { get; set; }
        public string session_id { get; set; }
        public int sequence_id { get; set; }
    }

    class UserInfo
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    class ProxyASR
    {
        private readonly int m_proxyPort;
        private readonly String m_asrIP;
        private readonly int m_asrPort;
        private readonly int m_sampleRate;

        private List<IWebSocketConnection> m_allSockets;
        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;
        private Dictionary<IWebSocketConnection, String> m_sock2sessionID;

        private CommASR m_commASR = null;

        private Dictionary<String, SessionHelper> m_sessionID2Helper;

        private DatabaseHelper m_databaseHelper;
        private ILogger _logger;

        //--------------------------------------------------------------------->
        // C'TOR: initialize member variables
        //--------------------------------------------------------------------->
        public ProxyASR(IConfiguration config, DatabaseHelper databaseHelper)
        {
            //databaseHelper = new DatabaseHelper(config);
            _logger = LogHelper.GetLogger<ProxyASR>();

            m_proxyPort = Int32.Parse(config.GetSection("Proxy")["proxyPort"]);
            m_asrIP = config.GetSection("Proxy")["asrIP"];
            m_asrPort = Int32.Parse(config.GetSection("Proxy")["asrPort"]);
            m_sampleRate = Int32.Parse(config.GetSection("Proxy")["samplerate"]);
            m_allSockets = new List<IWebSocketConnection>();
            m_sessionID2sock = new Dictionary<String, IWebSocketConnection>();
            m_sock2sessionID = new Dictionary<IWebSocketConnection, String>();
            m_sessionID2Helper = new Dictionary<String, SessionHelper>();
            m_databaseHelper = databaseHelper;

            m_commASR = CommASR.Create(m_asrIP, m_asrPort, m_sampleRate, m_sessionID2sock, m_sessionID2Helper, databaseHelper);
        }


        public List<IWebSocketConnection> allSockets
        {
            get { return m_allSockets; }
        }


        public void Start()
        {
            FleckLog.Level = LogLevel.Error;

            var server = new WebSocketServer("ws://0.0.0.0:" + m_proxyPort);
            _logger.Information("Starting Fleck WebSocket Server...");
            _logger.Information("Service_Port: " + m_proxyPort + "; ASR_Port: " + m_asrPort + ";  ASR_IP: " + m_asrIP +  ";  Samplerate: " + m_sampleRate);

            server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        OnConnect(socket);
                    };

                    socket.OnClose = () =>
                    {
                        OnDisconnect(socket);
                    };

                    socket.OnMessage = message =>
                    {
                        OnMessage(socket, message);
                    };

                    socket.OnBinary = data =>
                    {
                        OnBinaryData(socket, data);
                    };
                }
            );
        }

        //--------------------------------------------------------------------->
        // Handle new websocket connect
        //--------------------------------------------------------------------->
        private void OnConnect(IWebSocketConnection sock)
        {
            _logger.Information("WS Connect...");

            // authenticate client
            try
            {
                string authHeader = sock.ConnectionInfo.Headers["Authorization"];
                Console.WriteLine(authHeader);

                bool is_user = IsUser(authHeader);

                if (is_user)
                {
                    var session = new SessionHelper();
                    sock.Send("0{\"session_id\": \"" + session.m_sessionID + "\"}");
                    m_sessionID2Helper[session.m_sessionID] = session;
                    m_allSockets.Add(sock);
                    return;
                }
            }
            catch (System.Exception exception)
            {
                _logger.Error(exception, exception.Message);
            }

            _logger.Error("Fail to verify user. Closing socket connection.");
            sock.Close();
        }

        //--------------------------------------------------------------------->
        // Handle websocket disconnect
        //--------------------------------------------------------------------->
        private void OnDisconnect(IWebSocketConnection sock)
        {
            _logger.Information("WS Disconnect...");
            // Disconnect from ASR engine
            var session_id = m_sock2sessionID[sock];
            if (String.IsNullOrEmpty(session_id) == false)
            {
                m_commASR.DisconnectASR(session_id);
            }
            
            m_allSockets.Remove(sock);
        }

        //--------------------------------------------------------------------->
        // Handle websocket text message
        //--------------------------------------------------------------------->
        private void OnMessage(IWebSocketConnection sock, String msg)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _logger.Information(msg);

            if (msg.Contains("right_text")==false || msg.Contains("session_id")==false || msg.Contains("sequence_id") == false)
            {
                _logger.Error("message missing 'right_text' OR 'session_id' OR 'sequence_id' .... ignored ....");
                return;
            }

            AnswerPlusSessionID aps = JsonConvert.DeserializeObject<AnswerPlusSessionID>(msg);
            
            if (m_sock2sessionID.ContainsKey(sock)==false)
            {
                //------------------------------------------------------------->
                // it is a new session from client:
                //      (1) create websocket connection to ASR Engine
                //------------------------------------------------------------->
                m_sock2sessionID[sock] = aps.session_id;
                m_sessionID2sock[aps.session_id] = sock;

                _logger.Information("map sock -> sessionID: " + aps.session_id);
                _logger.Information("map sessionID " + aps.session_id + " -> sock");

                m_commASR.ConnectASR(aps.session_id);
            }

            //----------------------------------------------------------------->
            // Store session and sequence information
            //----------------------------------------------------------------->
            try
            {
                var session = m_sessionID2Helper[aps.session_id];
                if (session.m_sequence2inputword.ContainsKey(aps.sequence_id) == false)
                {
                    session.m_sequence2inputword[aps.sequence_id] = aps.right_text;
                    session.m_sequenceQueue.Enqueue(aps.sequence_id);
                    session.m_sequenceStartTime[aps.sequence_id] = DateTime.UtcNow;
                    session.m_sequencePredictionResult[aps.sequence_id] = new List<string>();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Retrieve Session informatino error: " + e.ToString());
            }


            //----------------------------------------------------------------->
            // Initialize recrod into database
            //----------------------------------------------------------------->

            // update asr_audio_stream_prediction
            if (m_databaseHelper.ConnectionStatus == true)
            {
                m_databaseHelper.InsertAudioStreamPrediction(
                    user_id: 1,
                    device_id: "device1",
                    app_id: 1,
                    session_id: aps.session_id,
                    seq_id: aps.sequence_id,
                    pred_timestamp: DateTime.UtcNow,
                    input_word: aps.right_text,
                    return_text: "");

                m_databaseHelper.InsertAudioStreamInfo(
                    user_id: 1,
                    device_id: "device1",
                    app_id: 1,
                    session_id: aps.session_id,
                    seq_id: aps.sequence_id,
                    proc_start_time: DateTime.UtcNow,
                    proc_end_time: DateTime.UtcNow,
                    stream_duration: 0);
            }

        }

        //--------------------------------------------------------------------->
        // Handle websocket text message
        //--------------------------------------------------------------------->
        private void OnBinaryData(IWebSocketConnection sock, byte[] data)
        {
            var sessionID = m_sock2sessionID[sock];
            m_commASR.SendBinaryData(sessionID, data);

            var session = m_sessionID2Helper[sessionID];
            var sequenceID = session.GetCurrentSequenceID();
            session.StoreIncommingBytes(sequenceID, data);
        }


        private bool IsUser(string authString)
        {
            if (authString.Contains("Basic"))
            {
                string encodedCredentials = authString.Split(' ')[1];
                string[] decodedCredentials = Encoding.UTF8
                    .GetString(Convert.FromBase64String(encodedCredentials))
                    .Split(':');
                string username = decodedCredentials[0];
                string password = decodedCredentials[1];

                bool is_user = m_databaseHelper.Is_User(username, password);

                return is_user;
            }

            return false;
        }
    }

}
