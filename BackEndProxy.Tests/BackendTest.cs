using System;
using database_and_log;
using System.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;
using BackendProxy2ASR;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text;

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
        private string proxyServerUrl;
        private readonly ITestOutputHelper output;
        public BackEndProxy_IntegrationTesting(BackEndProxyFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;

            var proxyPort = Int32.Parse(this.fixture.config.GetSection("Proxy")["proxyPort"]);
            var proxyHost = this.fixture.config.GetSection("Proxy")["proxyHost"];
            this.proxyServerUrl = $"ws://{proxyHost}:{proxyPort}";
        }

        [Fact]
        public void SimpleTest()
        {
            NpgsqlDataReader playbackReader = this.fixture.databaseHelper.GetPlayback("simple_playback");
            bool endTest = false;
            string asrfullResult = "";

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl);

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

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl);

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

    public class BackEndProxy_AuthTesting : IClassFixture<BackEndProxyFixture>
    {
        BackEndProxyFixture fixture;
        private string proxyServerUrl;
        private readonly ITestOutputHelper output;
        public BackEndProxy_AuthTesting(BackEndProxyFixture fixture, ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;

            var proxyPort = Int32.Parse(this.fixture.config.GetSection("Proxy")["proxyPort"]);
            var proxyHost = this.fixture.config.GetSection("Proxy")["proxyHost"];
            this.proxyServerUrl = $"ws://{proxyHost}:{proxyPort}";
        }

        [Fact]
        public void TestWithNoCredentials()
        {
            List<string> response = new List<string>();
            string expected = "Fail to authenticate user. Closing socket connection.";

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl);

            wsw.OnMessage((msg, sock) =>
            {
                this.output.WriteLine(msg);
                response.Add(msg);
            });

            Task task = Task.Run(async () =>
            {
                wsw.Connect();
                await Task.Delay(3000);

                if (wsw.GetWebSocketState() == WebSocketState.Open)
                {
                    await wsw.Disconnect();
                }
            });

            task.Wait();
            Assert.Equal(response[0], expected);
        }

        [Fact]
        public void DatabaseTestWithCorrectCredentials()
        {
            Dictionary<string, string> headerOptions = new Dictionary<string, string>();
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("username_1:test_password_1");
            headerOptions["Authorization"] = $"Basic {System.Convert.ToBase64String(plainTextBytes)}";
            string response = "";

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl, headerOptions);

            wsw.OnMessage((msg, sock) =>
            {
                this.output.WriteLine(msg);
                response = msg;
            });

            Task task = Task.Run(async () =>
            {
                wsw.Connect();
                await Task.Delay(3000);
                if (wsw.GetWebSocketState() == WebSocketState.Open)
                {
                    await wsw.Disconnect();
                }
            });

            task.Wait();
            Assert.Equal("0", response[0].ToString());
            Assert.Contains("session_id", response);
        }

        [Fact]
        public void DatabaseTestWithWrongCredentials()
        {
            Dictionary<string, string> headerOptions = new Dictionary<string, string>();
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes("wrong_user:wrong_password");
            headerOptions["Authorization"] = $"Basic {System.Convert.ToBase64String(plainTextBytes)}";
            List<string> response = new List<string>();
            string expected = "Fail to authenticate user. Closing socket connection.";

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl, headerOptions);

            wsw.OnMessage((msg, sock) =>
            {
                this.output.WriteLine(msg);
                response.Add(msg);
            });

            Task task = Task.Run(async () =>
            {
                wsw.Connect();
                await Task.Delay(3000);

                if (wsw.GetWebSocketState() == WebSocketState.Open)
                {
                    await wsw.Disconnect();
                }
            });

            task.Wait();
            Assert.Equal(response[0], expected);
        }

        [Fact]
        public async void Auth0TestWithCorrectCredentials()
        {
            Dictionary<string, string> headerOptions = new Dictionary<string, string>();

            var authSection = this.fixture.config.GetSection("Auth");
            string authURL = authSection["AuthURL"];
            string clientId = authSection["ClientID"];
            string clientSecret = authSection["ClientSecret"];
            string audience = authSection["Audience"];
            string grantType = authSection["GrantType"];
            string content = $@"{{
                ""client_id"":""{clientId}"",
                ""client_secret"":""{clientSecret}"",
                ""audience"":""{audience}"",
                ""grant_type"":""{grantType}""
                }}
            ";

            // get access token from Auth0 authentication server
            HttpClient client = new HttpClient();
            var stringContent = new StringContent(content, Encoding.UTF8, "application/json");

            HttpResponseMessage authResponse = await client.PostAsync(authURL, stringContent);
            authResponse.EnsureSuccessStatusCode();
            string responseBody = await authResponse.Content.ReadAsStringAsync();
            string accessToken = (string)JObject.Parse(responseBody)["access_token"];

            // test proxy server
            headerOptions["Authorization"] = $"Bearer {accessToken}";
            string response = "";

            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl, headerOptions);

            wsw.OnMessage((msg, sock) =>
            {
                this.output.WriteLine(msg);
                response = msg;
            });

            Task task = Task.Run(async () =>
            {
                wsw.Connect();
                await Task.Delay(3000);
                if (wsw.GetWebSocketState() == WebSocketState.Open)
                {
                    await wsw.Disconnect();
                }
            });

            task.Wait();
            Assert.NotEmpty(response);
            Assert.Equal("0", response[0].ToString());
            Assert.Contains("session_id", response);
        }

        [Fact]
        public void Auth0TestWithWrongCredentials()
        {
            Dictionary<string, string> headerOptions = new Dictionary<string, string>();

            // random JWT token encoded using RS256 from https://jwt.io/
            string accessToken = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxM"
                + "jM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiYWRtaW4iOnRydWUsImlhdCI6MTUxNjIzOTAyMn0."
                + "POstGetfAytaZS82wHcjoTyoqhMyxXiWdR7Nn7A29DNSl0EiXLdwJ6xC6AfgZWF1bOsS_TuYI3OG85"
                + "AmiExREkrS6tDfTQ2B3WXlrr-wp5AokiRbz3_oB4OxG-W9KcEEbDRcZc0nH3L7LzYptiy1PtAylQGx"
                + "HTWZXtGz4ht0bAecBgmpdgXMguEIcoqPJ1n3pIWk_dUZegpqx0Lka21H6XxUTxiy8OcaarA8zdnPUnV"
                + "6AmNP3ecFawIFYdvJB_cm-GvpCSbr8G8y_Mllj8f4x9nBH8pQux89_6gUY618iYv7tuPWBFfEbLxtF"
                + "2pZS6YC1aSfLQxeNe8djT9YjpvRZA";

            // test proxy server
            headerOptions["Authorization"] = $"Bearer {accessToken}";
            WebSocketWrapper wsw = WebSocketWrapper.Create(this.proxyServerUrl, headerOptions);

            List<string> response = new List<string>();
            string expected = "Fail to authenticate user. Closing socket connection.";

            wsw.OnMessage((msg, sock) =>
            {
                this.output.WriteLine(msg);
                response.Add(msg);
            });

            Task task = Task.Run(async () =>
            {
                wsw.Connect();
                await Task.Delay(3000);

                if (wsw.GetWebSocketState() == WebSocketState.Open)
                {
                    await wsw.Disconnect();
                }
            });

            task.Wait();
            Assert.NotEmpty(response);
            Assert.Equal(response[0], expected);
        }
    }
}
