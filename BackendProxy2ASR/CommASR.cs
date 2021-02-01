using System;
using System.Collections.Generic;
using System.Text;

using Fleck;
using database_and_log;
using Serilog;
using Newtonsoft.Json;


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

        private DatabaseHelper databaseHelper = new DatabaseHelper("../config.json");

        private ILogger _logger = new LogHelper<CommASR>("../config.json").Logger;

        protected CommASR(String asrIP, int asrPort, int sampleRate, Dictionary<String, IWebSocketConnection> sessionID2sock, Dictionary<String, SessionHelper> sessionID2helper)
        {
            m_asrIP = asrIP;
            m_asrPort = asrPort;
            m_sampleRate = sampleRate;
            m_sessionID2sock = sessionID2sock;
            m_sessionID2helper = sessionID2helper;

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
        public static CommASR Create(String ip, int port, int sampleRate, Dictionary<String, IWebSocketConnection> sessionID2sock, Dictionary<String, SessionHelper> sessionID2helper)
        {
            return new CommASR(ip, port, sampleRate, sessionID2sock, sessionID2helper);
        }

        public void ConnectASR(String sessionID)
        {
            var uri = "wss://" + m_asrIP + ":" + m_asrPort + "/ws/streamraw/" + m_sampleRate;

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
                    var ProxySocket = m_sessionID2sock[sessionID];
                    ProxySocket.Send(msg);
                    InsertPredictionToDB(msg, sessionID);
                }
            );

            wsw.OnDisconnect((sock) =>
                {
                    SendEndStream(sessionID);
                    m_sessionID2wsWrap.Remove(sessionID);
                    m_wsWrap2sessionID.Remove(sock);
                    _logger.Information("Disconnected from ASR Engine: sessionID = " + sessionID);
                }
            );

            wsw.Connect();
        }

        //----------------------------------------------------------------------------------------->
        // disconnect from ASR when client disconnect
        //----------------------------------------------------------------------------------------->
        public void DisconnectASR(String sessionID)
        {
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
            {
                var wsw = m_sessionID2wsWrap[sessionID];
                wsw.Disconnect();
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
        public void SendStartStream(String sessionID)
        {
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send start packet -> 1-byte = 0;
                //----------------------------------------------->
                byte[] start = new byte[1];
                start[0] = 0;
                m_sessionID2wsWrap[sessionID].SendBytes(start);

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
        public void SendEndStream(String sessionID)
        {
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send start packet -> 1-byte = 0;
                //----------------------------------------------->
                byte[] end = new byte[1];
                end[0] = 1;
                m_sessionID2wsWrap[sessionID].SendBytes(end);

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
        public void SendBinaryData(String sessionID, byte[] data)
        {
            if (m_sessionID2wsWrap.ContainsKey(sessionID) == true)
            {
                //----------------------------------------------->
                //send actual voice stream
                //----------------------------------------------->
                m_sessionID2wsWrap[sessionID].SendBytes(data);
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

                if (ASRResult.result != "" && ASRResult.cmd == "asrfull")
                {

                    bool connectionResult = databaseHelper.Open();
                    _logger.Information("Opening connection success? : " + connectionResult.ToString());
                    if (connectionResult)
                    {
                        DateTime startTime = session.m_sequenceStartTime[sequenceID];
                        DateTime endTime = DateTime.UtcNow;
                        byte[] input_audio = session.RetrieveSequenceBytes(sequenceID);

                        // update audio prediction result
                        databaseHelper.UpdateAudioStreamPrediction(
                            session_id: sessionID,
                            seq_id: sequenceID,
                            pred_timestamp: endTime,
                            return_text: ASRResult.result
                            );
                        // ipdate audio stream info
                        databaseHelper.UpdateAudioStreamInfo(
                            session_id: sessionID,
                            seq_id: sequenceID,
                            proc_end_time: endTime,
                            stream_duration: (long)(endTime - startTime).TotalMilliseconds
                            );
                    }

                    connectionResult = databaseHelper.Close();
                    _logger.Information("Closing connection success? : " + connectionResult.ToString());
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
            }
        }
    }
}
