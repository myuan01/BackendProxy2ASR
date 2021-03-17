using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Fleck;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using Jose;
using System.Security.Cryptography;
using System.Net;

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

    class ProxyASR
    {
        private readonly int m_proxyPort;
        private readonly String m_asrIP;
        private readonly int m_asrPort;
        private readonly int m_sampleRate;
        private readonly bool m_toAuthenticate;
        private readonly string m_authMethod;
        private readonly string m_authDomain;
        private readonly string m_authAudience;

        private List<IWebSocketConnection> m_allSockets;
        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;
        private Dictionary<IWebSocketConnection, String> m_sock2sessionID;
        private Dictionary<String, CancellationTokenSource> m_sessonId2PingCancellationTokenSource;

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
            m_authMethod = ConfigurationBinder.GetValue<string>(config.GetSection("Auth"), "AuthMethod", "");
            m_authDomain = ConfigurationBinder.GetValue<string>(config.GetSection("Auth"), "Auth0Domain", "");
            m_authAudience = ConfigurationBinder.GetValue<string>(config.GetSection("Auth"), "Audience", "");

            m_allSockets = new List<IWebSocketConnection>();
            m_sessionID2sock = new Dictionary<String, IWebSocketConnection>();
            m_sock2sessionID = new Dictionary<IWebSocketConnection, String>();
            m_sessionID2Helper = new Dictionary<String, SessionHelper>();
            m_sessonId2PingCancellationTokenSource = new Dictionary<String, CancellationTokenSource>();
            m_databaseHelper = databaseHelper;

            m_commASR = CommASR.Create(config, databaseHelper, m_sessionID2sock, m_sessionID2Helper);
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

            server.Start(socket =>
                {
                    socket.OnOpen = () =>
                    {
                        OnConnect(socket);
                    };

                    socket.OnClose = async () =>
                    {
                        await OnDisconnect(socket);
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
                    is_user = IsUser(authHeader, m_authMethod);
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

                    // start ping to client
                    CancellationTokenSource tokenSource = new CancellationTokenSource();
                    m_sessonId2PingCancellationTokenSource[session.m_sessionID] = tokenSource;
                    Task.Run(() => StartPing(sock, tokenSource.Token), tokenSource.Token);

                    // connect to asr engine
                    if (session.IsConnectedToASR == false)
                    {
                        try
                        {
                            var wsIndex = m_commASR.ConnectASR(session.m_sessionID);
                            if (wsIndex == 0)
                            {
                                throw new Exception("No avaliable ASR socket at the moment. Please try again later..");
                            }
                            session.IsConnectedToASR = true;
                            _logger.Information("Successfully connected to ASR Engine for session: " + session.m_sessionID + " with socket " + wsIndex);
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
        private async Task OnDisconnect(IWebSocketConnection sock)
        {
            // Disconnect from ASR engine
            if (m_sock2sessionID.ContainsKey(sock))
            {
                var session_id = m_sock2sessionID[sock];
                _logger.Information($"Disconnecting WS for SessionID = {session_id}");

                m_sessonId2PingCancellationTokenSource[session_id].Cancel();
                m_sessonId2PingCancellationTokenSource.Remove(session_id);

                await m_commASR.DisconnectASR(session_id);
                m_sessionID2Helper.Remove(session_id);
                m_sessionID2sock.Remove(session_id);
                m_sock2sessionID.Remove(sock);
                m_allSockets.Remove(sock);
            }
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
                    var wsIndex = m_commASR.ConnectASR(session.m_sessionID);
                    if (wsIndex == 0)
                    {
                        throw new Exception("No avaliable ASR socket at the moment. Please try again later..");
                    }
                    session.IsConnectedToASR = true;
                    _logger.Information("Successfully connected to ASR Engine for session: " + session.m_sessionID + " with socket " + wsIndex);
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
        private async void OnBinaryData(IWebSocketConnection sock, byte[] data)
        {
            if (!m_allSockets.Contains(sock))
            {
                _logger.Error("User not authenticated!");
                return;
            }


            var session_id = m_sock2sessionID[sock];
            //var wsIndex = m_sessionID2ASRSocket[session_id];

            try
            {
                //_logger.Information("Sending binary data for session " + session_id + "....");
                await m_commASR.SendBinaryData(session_id, data);
                m_databaseHelper.insert_playback(data, session_id);
            }
            catch (Exception ex)
            {
                string errorMessage = "ASR websocket is not open. Disconnect from client: " + ex.Message;
                _logger.Error(errorMessage);
                await sock.Send(errorMessage);
                sock.Close();
            }

            var session = m_sessionID2Helper[session_id];
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
        private bool IsUser(string authString, string authMethod)
        {
            if (authMethod == "database" && authString.ToLower().Contains("basic"))
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
            else if (authMethod == "auth0" && authString.ToLower().Contains("bearer"))
            {
                try
                {
                    // https://github.com/dvsekhvalnov/jose-jwt#two-phase-validation
                    string token = authString.Split(' ')[1];

                    using (WebClient wc = new WebClient())
                    {
                        //step 0: Get public key
                        var json = wc.DownloadString($"https://{m_authDomain}/.well-known/jwks.json");
                        JObject jsonObject = JObject.Parse(json);

                        // step 1a: get headers info
                        var headers = Jose.JWT.Headers(token);

                        // step 1b: lookup validation key based on header info
                        JObject jwk = new JObject();
                        foreach (JObject item in jsonObject["keys"])
                        {
                            if ((string)item["kid"] == (string)headers["kid"])
                            {
                                jwk = item;
                            }
                        }

                        // step 1c: load the JWK data into an RSA key
                        RSACryptoServiceProvider key = new RSACryptoServiceProvider();
                        key.ImportParameters(new RSAParameters
                        {
                            Modulus = Base64Url.Decode((string)jwk["n"]),
                            Exponent = Base64Url.Decode((string)jwk["e"])
                        });

                        var payload = JObject.Parse(Jose.JWT.Decode(token, key));

                        // verify audience and issuer
                        string audience = (string)payload["aud"];
                        string issuer = (string)payload["iss"];

                        if (audience != this.m_authAudience)
                        {
                            throw new Exception("Incorrect audience");
                        }
                        if (issuer != $"https://{this.m_authDomain}/")
                        {
                            throw new Exception("Incorrect issuer");
                        }

                        return true;
                    }
                }
                catch (System.Exception exception)
                {
                    _logger.Error(exception, exception.Message);
                    return false;
                }
            }

            return false;
        }

        private async Task StartPing(IWebSocketConnection sock, CancellationToken token)
        {
            var session_id = m_sock2sessionID[sock];

            if (token.IsCancellationRequested)
            {
                return;
            }

            while (sock.IsAvailable)
            {
                if (token.IsCancellationRequested)
                {
                    _logger.Information($"Stopping task pinging for SessionID = {session_id}");
                    return;
                }
                _logger.Information($"Sending ping for SessionID = {session_id}");
                await Task.Run(() => sock.SendPing(_pingMessage));
                await Task.Delay(TimeSpan.FromMilliseconds(_pingInterval));
            }

            if (token.IsCancellationRequested)
            {
                _logger.Information($"Stopping task sending ping for SessionID = {session_id}");
                return;
            }

            _logger.Error($"Unable to receive pong from client for SessionID = {session_id}. Disconnecting from client...");
            sock.Close();
        }
    }
}
