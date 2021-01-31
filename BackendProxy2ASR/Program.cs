using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BackendProxy2ASR
{
    class Program
    {
        static void Main(string[] args)
        {
            //args = { "8008", "127.0.0.1", "7000", "16000"};
            if (args.Length != 4)
            {
                Console.WriteLine("Usage: BackendProxy2ASR.exe <proxyPort> <asrIP> <asrPort> <samplerate>");
                return;
            }

            int proxyPort = Int32.Parse(args[0]);
            String asrIP = args[1];
            int asrPort = Int32.Parse(args[2]);
            int sampleRate = Int32.Parse(args[3]);
            
            ProxyASR proxy = new ProxyASR(proxyPort, asrIP, asrPort, sampleRate);
            proxy.Start();

            var input = Console.ReadLine();
            Console.WriteLine(input);
            while (input != "exit")
            {
                foreach (var socket in proxy.allSockets.ToList())
                {
                    socket.Send(input);
                }
                input = Console.ReadLine();
            }
        }
    }
}
