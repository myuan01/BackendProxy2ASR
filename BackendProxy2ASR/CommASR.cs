using System;
using System.Collections.Generic;
using System.Net.WebSockets;
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
        private Dictionary<String, IWebSocketConnection> m_sessionID2sock;  //sessionID -> client websocket
        private Dictionary<String, SessionHelper> m_sessionID2helper;  //sessionID -> session helper

        private Dictionary<String, WebSocketWrapper> m_sessionID2wsWrap;
        private Dictionary<WebSocketWrapper, String> m_wsWrap2sessionID;

        private DatabaseHelper m_databaseHelper;

        private ILogger _logger = LogHelper.GetLogger<CommASR>();

        protected CommASR(String asrIP, int asrPort, int sampleRate, Dictionary<String, IWebSocketConnection> sessionID2sock, 
            Dictionary<String, SessionHelper> sessionID2helper, DatabaseHelper databaseHelper)
        {
            m_asrIP = asrIP;
            m_asrPort = asrPort;
            m_sampleRate = sampleRate;
            m_sessionID2sock = sessionID2sock;
            m_sessionID2helper = sessionID2helper;
            m_databaseHelper = databaseHelper;

            m_sessionID2wsWrap = new Dictionary<String, WebSocketWrapper>();
            m_wsWrap2sessionID = new Dictionary<WebSocketWrapper, String>();

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

        //----------------------------------------------------------------------------------------->
        // Creates a new instance
        //----------------------------------------------------------------------------------------->
        public static CommASR Create(String ip, int port, int sampleRate, Dictionary<String, IWebSocketConnection> sessionID2sock, 
            Dictionary<String, SessionHelper> sessionID2helper, DatabaseHelper databaseHelper)
        {
            return new CommASR(ip, port, sampleRate, sessionID2sock, sessionID2helper, databaseHelper);
        }

        public void ConnectASR(String sessionID)
        {
            var uri = "wss://" + m_asrIP + ":" + m_asrPort + "/ws/streamraw/" + m_sampleRate;
            //var uri = "ws://" + m_asrIP + ":" + m_asrPort;

            _logger.Information("entered CommASR::ConnectASR(" + sessionID + "): uri = " + uri);

            var wsw = WebSocketWrapper.Create(uri);

            wsw.OnConnect((sock) =>
                {
                    _logger.Information("Connected to ASR Engine: sessionID = " + sessionID);
                    m_sessionID2wsWrap[sessionID] = sock;
                    m_wsWrap2sessionID[sock] = sessionID;
                    SendStartStream(sessionID);
                }
            );

            wsw.OnMessage((msg, sock) =>
                {
                    _logger.Information("EngineASR -> CommASR: " + msg + "  [sessionID = " + sessionID + ", sessionID = " + m_wsWrap2sessionID[sock] + "]");
                    if (msg.Length == 0) return;
                    var ProxySocket = m_sessionID2sock[sessionID];
                    ProxySocket.Send(msg);
                    InsertPredictionToDB(msg, sessionID);
                }
            );

            wsw.OnDisconnect((sock) =>
                {
                    if (m_sessionID2wsWrap.ContainsKey(sessionID))
                    {
                        m_sessionID2wsWrap.Remove(sessionID);
                        m_wsWrap2sessionID.Remove(sock);
                        _logger.Information("Disconnected from ASR Engine: sessionID = " + sessionID);
                    }
                }
            );

            try
            {
                wsw.Connect();
            }
            catch (Exception ex)
            {
                _logger.Error("Unable to connect to ASR Engine: " + ex.Message);
            }
            
        }

        //----------------------------------------------------------------------------------------->
        // disconnect from ASR when client disconnect
        //----------------------------------------------------------------------------------------->
        public async Task DisconnectASR(String sessionID)
        {
            if (m_sessionID2wsWrap.ContainsKey(sessionID))
            {
                var wsw = m_sessionID2wsWrap[sessionID];
                await SendEndStream(sessionID);
                await wsw.Disconnect();
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
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
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
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
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
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send actual voice stream
                //----------------------------------------------->
                try
                {
                    var wsw = m_sessionID2wsWrap[sessionID];
                    if (wsw.GetWebSocketState() != WebSocketState.Open)
                    {
                        _logger.Information("ASR state: " + wsw.GetWebSocketState().ToString());
                        _logger.Error("ASR Websocket is not open.");
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
