using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
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
        private readonly bool m_toAuthenticate;

        private List<IWebSocketConnection> m_allSockets;
        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;
        private Dictionary<IWebSocketConnection, String> m_sock2sessionID;

        private CommASR m_commASR = null;

        private Dictionary<String, SessionHelper> m_sessionID2Helper;

        private DatabaseHelper m_databaseHelper;
        private ILogger _logger;

        // flag for ping-pong message
        private static readonly byte[] _pingMessage = { 2, 3 };
        private static double _pingInterval = 10000;

        //--------------------------------------------------------------------->
        // C'TOR: initialize member variables
        //--------------------------------------------------------------------->
        public ProxyASR(IConfiguration config, DatabaseHelper databaseHelper)
        {
            //databaseHelper = new DatabaseHelper(config);
            _logger = LogHelper.GetLogger<ProxyASR>();

            // load config values
            m_proxyPort = Int32.Parse(config.GetSection("Proxy")["proxyPort"]);
            m_asrIP = config.GetSection("Proxy")["asrIP"];
            m_asrPort = Int32.Parse(config.GetSection("Proxy")["asrPort"]);
            m_sampleRate = Int32.Parse(config.GetSection("Proxy")["samplerate"]);
            m_toAuthenticate = ConfigurationBinder.GetValue<bool>(config.GetSection("Auth"), "ToAuthenticate", true);

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
            _logger.Information("Service_Port: " + m_proxyPort + "; ASR_Port: " + m_asrPort + ";  ASR_IP: " + m_asrIP + ";  Samplerate: " + m_sampleRate);

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

                    if (socket.IsAvailable == false)
                    {
                        Console.WriteLine("socket not avaliable");
                    }

                    socket.OnPong = pong =>
                    {
                        OnPong(socket, pong);
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
                bool is_user = false;
                if (m_toAuthenticate)
                {
                    string authHeader = sock.ConnectionInfo.Headers["Authorization"];
                    is_user = IsUser(authHeader);
                }
                else
                {
                    _logger.Information("Not authenticating user...");
                    is_user = true;
                }

                if (is_user)
                {
                    var session = new SessionHelper();
                    sock.Send("0{\"session_id\": \"" + session.m_sessionID + "\"}");
                    m_sessionID2Helper[session.m_sessionID] = session;
                    m_allSockets.Add(sock);

                    m_sock2sessionID[sock] = session.m_sessionID;
                    m_sessionID2sock[session.m_sessionID] = sock;

                    _logger.Information("map sock -> sessionID: " + session.m_sessionID);
                    _logger.Information("map sessionID " + session.m_sessionID + " -> sock");

                    Task.Run(() => StartPing(sock));

                    // connect to asr engine
                    if (session.IsConnectedToASR == false)
                    {
                        try
                        {
                            m_commASR.ConnectASR(session.m_sessionID);
                            session.IsConnectedToASR = true;
                            _logger.Information("Successfully connected to ASR Engine for session: " + session.m_sessionID);
                        }
                        catch (WebSocketException ex)
                        {
                            string ASRerrorMessage = "Unable to connect to ASR engine. Exception found: " + ex.Message;
                            _logger.Error(ASRerrorMessage);
                            sock.Send(ASRerrorMessage);
                            sock.Close();
                        }
                    }
                    return;
                }
            }
            catch (System.Exception exception)
            {
                _logger.Error(exception, exception.Message);
            }

            string errorMessage = "Fail to authenticate user. Closing socket connection.";
            _logger.Error(errorMessage);
            sock.Send(errorMessage);
            sock.Close();
        }

        //--------------------------------------------------------------------->
        // Handle websocket disconnect
        //--------------------------------------------------------------------->
        private void OnDisconnect(IWebSocketConnection sock)
        {
            _logger.Information("WS Disconnect...");
            // Disconnect from ASR engine
            if (m_sock2sessionID.ContainsKey(sock) == true)
            {
                var session_id = m_sock2sessionID[sock];
                m_commASR.DisconnectASR(session_id);

                m_sessionID2Helper.Remove(session_id);
                m_sessionID2sock.Remove(session_id);
            }
            m_sock2sessionID.Remove(sock);
            m_allSockets.Remove(sock);
        }

        //--------------------------------------------------------------------->
        // Handle websocket text message
        //--------------------------------------------------------------------->
        private void OnMessage(IWebSocketConnection sock, String msg)
        {
            if (!m_allSockets.Contains(sock))
            {
                _logger.Error("User not authenticated!");
                return;
            }

            Console.OutputEncoding = Encoding.UTF8;
            _logger.Information(msg);

            if (msg.Contains("right_text") == false || msg.Contains("session_id") == false || msg.Contains("sequence_id") == false)
            {
                _logger.Error("message missing 'right_text' OR 'session_id' OR 'sequence_id' .... ignored ....");
                return;
            }

            AnswerPlusSessionID aps = JsonConvert.DeserializeObject<AnswerPlusSessionID>(msg);

            var session = m_sessionID2Helper[aps.session_id];

            if (session.IsConnectedToASR == false)
            {
                try
                {
                    m_commASR.ConnectASR(aps.session_id);
                    session.IsConnectedToASR = true;
                }
                catch (WebSocketException ex)
                {
                    string errorMessage = "Unable to connect to ASR engine. Exception found: " + ex.Message;
                    _logger.Error(errorMessage);
                    sock.Send(errorMessage);
                    sock.Close();
                }
            }

            //----------------------------------------------------------------->
            // Store session and sequence information
            //----------------------------------------------------------------->
            try
            {

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
            if (m_databaseHelper.IsConnected())
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
            if (!m_allSockets.Contains(sock))
            {
                _logger.Error("User not authenticated!");
                return;
            }

            var sessionID = m_sock2sessionID[sock];
            if (m_commASR.ASRWsState != System.Net.WebSockets.WebSocketState.Open)
            {
                string errorMessage = "ASR websocket is not open. Disconnect from client..";
                _logger.Error(errorMessage);
                sock.Send(errorMessage);
                sock.Close();
            }
            else
            {
                m_databaseHelper.insert_playback(data, sessionID);
                m_commASR.SendBinaryData(sessionID, data);
            }

            var session = m_sessionID2Helper[sessionID];
            var sequenceID = session.GetCurrentSequenceID();
            session.StoreIncommingBytes(sequenceID, data);
        }

        private void OnPong(IWebSocketConnection sock, byte[] data)
        {
            _logger.Information("Receive Pong from client.");
        }

        //--------------------------------------------------------------------->
        // Helper function that authenticates a user using value from
        // "Authorization" field in HTTP header
        //--------------------------------------------------------------------->
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

        private async Task StartPing(IWebSocketConnection sock)
        {

            while (sock.IsAvailable)
            {
                _logger.Information("Send ping...");
                await Task.Run(() => sock.SendPing(_pingMessage));
                await Task.Delay(TimeSpan.FromMilliseconds(_pingInterval));
            }

            _logger.Error("Unable to receive pong from client. Disconnect from client...");
            sock.OnClose();

        }
    }

}
