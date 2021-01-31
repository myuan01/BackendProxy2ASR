﻿using System;
using System.Collections.Generic;
using System.Text;

namespace BackendProxy2ASR
{
    class SessionHelper
    {
        public readonly string m_sessionID;
        public readonly DateTime SesssionStartTime;

        public Dictionary<int, string> m_sequence2uttID { get; set; }
        public Dictionary<string, int> m_uttID2sequence { get; set; }

        public Dictionary<int, string> m_sequence2inputword { get; set; }
        public Dictionary<int, DateTime> m_sequenceStartTime { get; set; }
        public Dictionary<int, List<byte>> m_sequenceBytes { get; set; }
        public Queue<int> m_sequenceQueue { get; set; }

        private int CurrentSequenceID;

        //--------------------------------------------------------------------->
        // C'TOR: initialize member variables
        //--------------------------------------------------------------------->
        public SessionHelper()
        {
            m_sessionID = CreateSessionID();
            SesssionStartTime = DateTime.UtcNow;

            // sequence <-> uttid
            m_sequence2uttID = new Dictionary<int, string>();
            m_uttID2sequence = new Dictionary<string, int>();

            //sequence information
            m_sequence2inputword = new Dictionary<int, string>();
            m_sequenceStartTime = new Dictionary<int, DateTime>();
            m_sequenceBytes = new Dictionary<int, List<byte>>();
            m_sequenceQueue = new Queue<int>();
        }

        //----------------------------------------------------------------------------------------->
        // Create session id and send to frontend
        //----------------------------------------------------------------------------------------->
        public string CreateSessionID()
        {
            StringBuilder sessionID = new StringBuilder("");

            // for testing propose only

            //Guid g = Guid.NewGuid();
            //sessionID.Append(g);
            sessionID.Append("90009");
            return sessionID.ToString();
        }

        //----------------------------------------------------------------------------------------->
        // Return current sequence id
        //----------------------------------------------------------------------------------------->
        public int GetCurrentSequenceID()
        {
            if (m_sequenceQueue.Count != 0)
            {
                CurrentSequenceID = m_sequenceQueue.Dequeue();
            }

            return CurrentSequenceID;
        }

        //----------------------------------------------------------------------------------------->
        // save bytes information to session
        //----------------------------------------------------------------------------------------->
        public void StoreIncommingBytes(int seqenceID, byte[] data)
        {
            if (m_sequenceBytes.ContainsKey(seqenceID) == false)
            {
                m_sequenceBytes[seqenceID] = new List<byte>();
            }
            m_sequenceBytes[seqenceID].AddRange(data);
        }

        //----------------------------------------------------------------------------------------->
        // retrieve audio bytes information
        //----------------------------------------------------------------------------------------->
        public byte[] RetrieveSequenceBytes(int seqenceID)
        {
            return m_sequenceBytes[seqenceID].ToArray();
        }
    }
}