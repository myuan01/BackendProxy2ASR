using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

using Fleck;
using database_and_log;
using Serilog;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;


namespace BackendProxy2ASR
{
    class CommASR
    {
        private readonly String m_asrIP;
        private readonly int m_asrPort;
        private readonly int m_sampleRate;
        private readonly int m_maxConnection;

        private readonly bool m_usingDummy;
        private readonly string m_dummyAsr;
        private readonly int m_dummyPort;

        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;  //sessionID -> client websocket
        private Dictionary<String, SessionHelper> m_sessionID2helper;  //sessionID -> session helper

        private Dictionary<int, WebSocketWrapper> m_wsWrapPoolDictionary;
        private Dictionary<int, WebSocketState> m_wsWrapState;
        private Dictionary<String, int> m_sessionID2wsPoolIndex;
        private Queue<int> m_wsPoolQueue;
        private DatabaseHelper m_databaseHelper;

        private ILogger _logger = LogHelper.GetLogger<CommASR>();
      
        protected CommASR(IConfiguration config, DatabaseHelper databaseHelper, 
            Dictionary<String, IWebSocketConnection> sessionID2sock, Dictionary<String, SessionHelper> sessionID2helper)
        {
            m_asrIP = config.GetSection("Proxy")["asrIP"];
            m_asrPort = Int32.Parse(config.GetSection("Proxy")["asrPort"]);
            m_sampleRate = Int32.Parse(config.GetSection("Proxy")["samplerate"]);
            m_maxConnection = Int32.Parse(config.GetSection("Proxy")["maxConnection"]);

            m_usingDummy = ConfigurationBinder.GetValue<bool>(config.GetSection("DummyServer"), "usingDummy", true);
            m_dummyAsr = config.GetSection("DummyServer")["dummyAsrIP"];
            m_dummyPort = Int32.Parse(config.GetSection("DummyServer")["dummyAsrPort"]);

            m_sessionID2sock = sessionID2sock;
            m_sessionID2helper = sessionID2helper;
            m_databaseHelper = databaseHelper;

            m_wsWrapPoolDictionary = new Dictionary<int, WebSocketWrapper>();
            m_wsWrapState = new Dictionary<int, WebSocketState>();
            m_sessionID2wsPoolIndex = new Dictionary<String, int>();
            m_wsPoolQueue = new Queue<int>();

            CreateASRConnectionPooling();

        }

        //----------------------------------------------------------------------------------------->
        // Deserialize prediction result
        //----------------------------------------------------------------------------------------->
        class PredictionResult
        {
            public string cmd { get; set; }
            public string uttID { get; set; }
            public string result { get; set; }
        }

        /// <summary>
        /// Create ASR socket pool and init queue for avaliable sockets
        /// </summary>
        private void CreateASRConnectionPooling()
        {
            var uri = "wss://" + m_asrIP + ":" + m_asrPort + "/ws/streamraw/" + m_sampleRate;
            if (m_usingDummy == true)
            {
                uri = "ws://" + m_dummyAsr + ":" + m_dummyPort;
                _logger.Information(" Using dummy ASR Engine::ConnectASR.");
            }

            for (int i = 1; i < m_maxConnection + 1; i ++)
            {
                m_wsWrapState[i] = WebSocketState.None;
                var wsw = InitASRWebSocket(i, uri);
                if (wsw != null)
                {
                    m_wsWrapPoolDictionary[i] = wsw;
                    _logger.Information("InitASRWebSocket::ConnectASR(" + i + "): uri = " + uri);
                    m_wsPoolQueue.Enqueue(i);
                    _logger.Information("ASRWebSocket: " + i + " has been added into pooling queue");
                }
                else
                {
                    _logger.Error("Unable to create ASR websocket for index: " + i);
                }
                // avoid ASR engine process crash
                Thread.Sleep(1000);
            }
        }
        /// <summary>
        /// Init websocket to ASR engine
        /// </summary>
        /// <param name="index"></param>
        /// <param name="uri"></param>
        /// <returns></returns>
        private WebSocketWrapper InitASRWebSocket(int index, string uri)
        {
            var wsw = WebSocketWrapper.Create(uri);

            wsw.OnConnect((sock) =>
            {
                m_wsWrapState[index] = WebSocketState.Open;
            }
            );

            wsw.OnDisconnect((sock) =>
            {
                m_wsWrapState[index] = WebSocketState.Closed;
            }

            );

            try
            {
                wsw.Connect();
            }
            catch (Exception ex)
            {
                _logger.Error("Unable to connect to ASR Engine: " + ex.Message);
                return null;
            }

            return wsw;
        }
        //----------------------------------------------------------------------------------------->
        // Creates a new instance
        //----------------------------------------------------------------------------------------->
        public static CommASR Create(IConfiguration config, DatabaseHelper databaseHelper, 
            Dictionary<String, IWebSocketConnection> sessionID2sock, Dictionary<String, SessionHelper> sessionID2helper)
        {
            return new CommASR(config, databaseHelper, sessionID2sock, sessionID2helper);
        }
        /// <summary>
        /// When a Proxy establish connection, CommASR assign one socket to it with refined on Message method
        /// </summary>
        /// <param name="sessionID"></param>
        /// <returns></returns>
        public int ConnectASR(String sessionID)
        {
            _logger.Information("Get ASR websocket from pooling queue...");
            if (m_wsPoolQueue.Count != 0)
            {
                while (m_wsPoolQueue.Count != 0)
                {
                    var wsIndex = m_wsPoolQueue.Peek();
                    if (m_wsWrapState[wsIndex] == WebSocketState.Open)
                    {
                        wsIndex = m_wsPoolQueue.Dequeue();
                        m_sessionID2wsPoolIndex[sessionID] = wsIndex;

                        var wsw = m_wsWrapPoolDictionary[wsIndex];
                        var ProxySocket = m_sessionID2sock[sessionID];

                        wsw.OnMessage((msg, sock) =>
                            {
                                _logger.Information("EngineASR -> CommASR: " + msg + "  [sessionID = " + sessionID);
                                if (msg.Length == 0) return;
                                ProxySocket.Send(msg);
                                InsertPredictionToDB(msg, sessionID);
                            }
                        );

                        SendStartStream(sessionID);
                        return wsIndex;
                    }
                    else
                    {
                        m_wsPoolQueue.Dequeue();
                    }
                }

                return 0;
            }
            else
            {
                _logger.Error("No socket is available now. Reject client connection..");
                return 0;
            }


        }
        //----------------------------------------------------------------------------------------->
        // disconnect from ASR when client disconnect, put wsIndex back to queue
        //----------------------------------------------------------------------------------------->
        public async Task DisconnectASR(String sessionID)
        {
            if (m_sessionID2wsPoolIndex.ContainsKey(sessionID) == true)
            {
                var wsIndex = m_sessionID2wsPoolIndex[sessionID];
                await SendEndStream(sessionID);
                m_wsPoolQueue.Enqueue(wsIndex);
                _logger.Information("DisconnectASR: sessionID = " + sessionID);

            }
            else
            {
                _logger.Error("Wrong sessioID when disconnecting from ASR: sessionID = " + sessionID);
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to ASR engine
        //----------------------------------------------------------------------------------------->
        public async void SendStartStream(String sessionID)
        {
            if (m_sessionID2wsPoolIndex.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send start packet -> 1-byte = 0;
                //----------------------------------------------->
                byte[] start = new byte[1];
                start[0] = 0;
                await SendBinaryData(sessionID, start);
                //m_sessionID2wsWrap[sessionID].SendBytes(start);

                _logger.Information("start pkt sent to start stream: sessionID = " + sessionID);
            }
            else
            {
                _logger.Error("wrong sessioID in SendStartStream: sessionID = " + sessionID);
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to ASR engine
        //----------------------------------------------------------------------------------------->
        public async Task SendEndStream(String sessionID)
        {
            if (m_sessionID2wsPoolIndex.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send start packet -> 1-byte = 0;
                //----------------------------------------------->
                byte[] end = new byte[1];
                end[0] = 1;
                await SendBinaryData(sessionID, end);
                //m_sessionID2wsWrap[sessionID].SendBytes(end);

                _logger.Information("end pkt sent to end stream: sessionID = " + sessionID);
            }
            else
            {
                _logger.Error("wrong sessioID in SendEndStream: sessionID = " + sessionID);
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to ASR engine
        //----------------------------------------------------------------------------------------->
        public async Task SendBinaryData(String sessionID, byte[] data)
        {
            if (m_sessionID2wsPoolIndex.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send actual voice stream
                //----------------------------------------------->
                try
                {
                    var wsIndex = m_sessionID2wsPoolIndex[sessionID];
                    var wsw = m_wsWrapPoolDictionary[wsIndex];
                    if (wsw.GetWebSocketState() != WebSocketState.Open)
                    {
                        _logger.Information("ASR state: " + wsw.GetWebSocketState().ToString());
                        _logger.Error("ASR Websocket is not open.");
                        m_wsWrapState[wsIndex] = wsw.GetWebSocketState();
                        await wsw.Disconnect();

                    }
                    else
                    {
                        await wsw.SendBytes(data);
                    }
                    
                }
                catch (Exception e)
                {
                    _logger.Error("Catch exception in sendbinarydata: " + e.Message);
                }
            }
            else
            {
                _logger.Error("wrong sessioID in SendBinaryData: sessionID = " + sessionID);
            }
        }

        //----------------------------------------------------------------------------------------->
        // Update prediction and information to database
        //----------------------------------------------------------------------------------------->
        public void InsertPredictionToDB(string msg, string sessionID)
        {
            PredictionResult ASRResult = JsonConvert.DeserializeObject<PredictionResult>(msg);

            try
            {
                var session = m_sessionID2helper[sessionID];
                var uttid = ASRResult.uttID;
                int sequenceID;

                if (session.m_uttID2sequence.ContainsKey(uttid) == true)
                {
                    sequenceID = session.m_uttID2sequence[uttid];
                }
                else
                {
                    sequenceID = session.GetCurrentSequenceID();
                    session.m_uttID2sequence[uttid] = sequenceID;
                }

                if (ASRResult.cmd == "asrfull")
                {
                    if (m_databaseHelper.IsConnected())
                    {
                        DateTime startTime = session.m_sequenceStartTime[sequenceID];
                        DateTime endTime = DateTime.UtcNow;
                        byte[] input_audio = session.RetrieveSequenceBytes(sequenceID);
                        var bytesLength = session.m_sequenceBytesLength[sequenceID];

                        //Add preduction result
                        session.m_sequencePredictionResult[sequenceID].Add(ASRResult.result);
                        var currentResult = String.Join(", ", session.m_sequencePredictionResult[sequenceID].ToArray());
                        _logger.Information("Result update to database: " + currentResult);

                        // update audio prediction result
                        m_databaseHelper.UpdateAudioStreamPrediction(
                            session_id: sessionID,
                            seq_id: sequenceID,
                            pred_timestamp: endTime,
                            return_text: currentResult
                            //return_text: ASRResult.result
                            );
                        // ipdate audio stream info
                        m_databaseHelper.UpdateAudioStreamInfo(
                            session_id: sessionID,
                            seq_id: sequenceID,
                            proc_end_time: endTime,
                            //stream_duration: (long)(endTime - startTime).TotalMilliseconds
                            stream_duration: (long)Math.Round(Decimal.Divide((decimal)bytesLength, m_sampleRate), 3)*1000
                            );
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
            }
        }
    }
}
