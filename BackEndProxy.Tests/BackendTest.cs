using System;
using database_and_log;
using System.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using BackendProxy2ASR;
using Microsoft.Extensions.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace BackEndProxy.Tests
{
    public class BackEndProxyFixture : IDisposable
    {
        // https://xunit.net/docs/shared-context
        public IConfiguration config { get; set; }
        public DatabaseHelper databaseHelper { get; set; }

        public BackEndProxyFixture()
        {
            // TODO: run backend server in a new task or process

            string solnPath = Environment.GetEnvironmentVariable("SLN_PATH");
            string configRelativePath = "./BackEndProxy.Tests/test_config.json";
            string configAbsPath = Path.Combine(solnPath, configRelativePath);

            this.config = new ConfigurationBuilder()
                            .AddJsonFile(path: configAbsPath, optional: false, reloadOnChange: true)
                            .Build();

            // Init LogHelper
            LogHelper.Initialize(config);

            // Init DatabaseHelper
            this.databaseHelper = new DatabaseHelper(this.config);
            this.databaseHelper.Open();
        }

        public void Dispose()
        {
            // TODO: end backend task when test is over
            // backend server ends with the process

            this.databaseHelper.Close();
        }
    }
    public class BackEndProxy_IntegrationTesting : IClassFixture<BackEndProxyFixture>
    {
        BackEndProxyFixture fixture;
        private readonly ITestOutputHelper output;
        public BackEndProxy_IntegrationTesting(BackEndProxyFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        [Fact]
        public void SimpleTest()
        {
            NpgsqlDataReader playbackReader = this.fixture.databaseHelper.GetPlayback("simple_playback");

            var proxyPort = Int32.Parse(this.fixture.config.GetSection("Proxy")["proxyPort"]);
            var proxyHost = this.fixture.config.GetSection("Proxy")["proxyHost"];

            // assumes there's a proxy server running locally
            string url = $"ws://{proxyHost}:{proxyPort}";
            bool endTest = false;
            string asrfullResult = "";

            WebSocketWrapper wsw = WebSocketWrapper.Create(url);

            Task task = new Task(async () =>
            {
                wsw.Connect();
                while (!endTest || !playbackReader.IsClosed) { }
                await wsw.Disconnect();
            });

            wsw.OnMessage(async (msg, sock) =>
                {
                    this.output.WriteLine(msg);
                    if (msg[0].ToString() == "0")
                    {
                        var substring = msg.Substring(1);
                        string session_id = JObject.Parse(substring).Value<string>("session_id");

                        //send session information as text message
                        Random rnd = new Random();
                        string textinfo = "{right_text:" + rnd.Next(1, 150).ToString() + ", session_id:\"" + session_id + "\", sequence_id:" + rnd.Next(1, 1000).ToString() + "}";
                        wsw.SendMessage(textinfo);

                        // some time is needed for proxy server to connect to ASR engine
                        await Task.Delay(1000);

                        if (!playbackReader.HasRows)
                        {
                            throw new Exception("No playback message obtained");
                        }

                        while (playbackReader.Read())
                        {
                            byte[] message = playbackReader.GetFieldValue<byte[]>(playbackReader.GetOrdinal("message"));
                            await wsw.SendBytes(message);
                            Double delay = playbackReader.GetFieldValue<Double>(playbackReader.GetOrdinal("delay"));

                            if (delay > 0)
                            {
                                await Task.Delay(Convert.ToInt32(delay));
                            }
                        }
                        playbackReader.Close();
                    }
                    else
                    {
                        string cmd = JObject.Parse(msg).Value<string>("cmd");
                        if (cmd == "asrfull")
                        {
                            asrfullResult = JObject.Parse(msg).Value<string>("result");
                            endTest = true;
                        }
                    }
                }
            );

            task.Start();
            while (!task.IsCompleted) { }

            Assert.True(asrfullResult == "新年快乐 ", $"{asrfullResult} does not match '新年快乐 '");
        }

        [Fact]
        public void MockGameSessionTest()
        {
            NpgsqlDataReader playbackReader = this.fixture.databaseHelper.GetPlayback("cb0bd39c-9482-4e71-b81a-cdf334c5c883");

            var proxyPort = Int32.Parse(this.fixture.config.GetSection("Proxy")["proxyPort"]);
            var proxyHost = this.fixture.config.GetSection("Proxy")["proxyHost"];

            // assumes there's a proxy server running locally
            string url = $"ws://{proxyHost}:{proxyPort}";
            WebSocketWrapper wsw = WebSocketWrapper.Create(url);

            string[] expectedResults = { "吉祥如意", "四海增辉", "睫毛", "六六大顺", "新年欢乐", "有志竟成", "飞黄腾达" };
            List<string> response = new List<string>();

            Task task = new Task(async () =>
            {
                wsw.Connect();
                while (!playbackReader.IsClosed) { }
                await wsw.Disconnect();
            });

            wsw.OnMessage(async (msg, sock) =>
                {
                    this.output.WriteLine(msg);
                    if (msg[0].ToString() == "0")
                    {
                        var substring = msg.Substring(1);
                        string session_id = JObject.Parse(substring).Value<string>("session_id");

                        //send session information as text message
                        Random rnd = new Random();
                        string textinfo = "{right_text:" + rnd.Next(1, 150).ToString() + ", session_id:\"" + session_id + "\", sequence_id:" + rnd.Next(1, 1000).ToString() + "}";
                        wsw.SendMessage(textinfo);

                        // some time is needed for proxy server to connect to ASR engine
                        await Task.Delay(1000);

                        if (!playbackReader.HasRows)
                        {
                            throw new Exception("No playback message obtained");
                        }

                        while (playbackReader.Read())
                        {
                            byte[] message = playbackReader.GetFieldValue<byte[]>(playbackReader.GetOrdinal("message"));
                            await wsw.SendBytes(message);
                            await Task.Delay(50);
                        }
                        playbackReader.Close();
                        await Task.Delay(500);
                    }
                    else
                    {
                        string cmd = JObject.Parse(msg).Value<string>("cmd");
                        if (cmd == "asrfull")
                        {
                            string asrfullResult = JObject.Parse(msg).Value<string>("result");
                            response.Add(asrfullResult.Trim());
                        }
                    }
                }
            );

            task.Start();
            while (!task.IsCompleted) { }

            Assert.Equal(expectedResults, response);
        }
    }
}
