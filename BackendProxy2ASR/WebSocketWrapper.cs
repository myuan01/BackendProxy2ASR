using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using database_and_log;
using Serilog;
using Microsoft.Extensions.Configuration;


namespace BackendProxy2ASR
{
    class WebSocketWrapper
    {
        private const int ReceiveChunkSize = 4096;
        private const int SendChunkSize = 4096;

        private readonly ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private Action<WebSocketWrapper> _onConnected;
        private Action<string, WebSocketWrapper> _onMessage;
        private Action<WebSocketWrapper> _onDisconnected;

        private ILogger _logger = LogHelper.GetLogger<WebSocketWrapper>();

        protected WebSocketWrapper(string uri)
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.Zero;
            _ws.Options.RemoteCertificateValidationCallback = delegate { return true; };
            _uri = new Uri(uri);
            _cancellationToken = _cancellationTokenSource.Token;
        }

        //----------------------------------------------------------------------------------------->
        // Creates a new instance.
        //      uri: The URI of the WebSocket server
        //----------------------------------------------------------------------------------------->
        public static WebSocketWrapper Create(string uri)
        {
            return new WebSocketWrapper(uri);
        }

        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper Connect()
        {
            ConnectAsync();
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been established.
        //      onConnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnConnect(Action<WebSocketWrapper> onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Disconnects from WebSocket server.
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper Disconnect()
        {
            if ((_ws.State == WebSocketState.Open))
            {
                CallOnDisconnected();
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cancellationToken);
                //CallOnDisconnected();
            }
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been terminated.
        //      onDisconnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnDisconnect(Action<WebSocketWrapper> onDisconnect)
        {
            _onDisconnected = onDisconnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public void SendBytes(byte[] bytes)
        {
            SendBytesAsync(bytes);
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when a messages has been received.
        //      onMessage: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnMessage(Action<string, WebSocketWrapper> onMessage)
        {
            _onMessage = onMessage;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        public void SendMessage(string message)
        {
            SendMessageAsync(message);
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      actual implementation for the associated public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        private async void SendMessageAsync(string message)
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = Encoding.UTF8.GetBytes(message);
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await _ws.SendAsync(new ArraySegment<byte>(messageBuffer, offset, count), WebSocketMessageType.Text, lastMessage, _cancellationToken);
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      the actual implementation for the public interface
        //----------------------------------------------------------------------------------------->
        private async void SendBytesAsync(byte[] bytes)
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = bytes;
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await _ws.SendAsync(new ArraySegment<byte>(bytes, offset, count), WebSocketMessageType.Binary, lastMessage, _cancellationToken);
            }
        }


        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      actual implementation for the associated public interface
        //----------------------------------------------------------------------------------------->
        private async void ConnectAsync()
        {
            await _ws.ConnectAsync(_uri, _cancellationToken);
            CallOnConnected();
            StartListen();
        }

        private async void StartListen()
        {
            var buffer = new byte[ReceiveChunkSize];
            CancellationTokenSource source = new CancellationTokenSource(1000);

            try
            {
                while (_ws.State == WebSocketState.Open )
                {
                    var stringResult = new StringBuilder();

                    WebSocketReceiveResult result = null;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);
                        //result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), source.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if (_ws.State == WebSocketState.Closed)
                            {
                                CallOnDisconnected();
                                _ws.Dispose();
                                return;
                            } else
                            {
                                await
                                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                                CallOnDisconnected();
                            }
                        }
                        else
                        {
                            var str = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            stringResult.Append(str);
                        }
                    } while (result != null && !result.EndOfMessage);

                    CallOnMessage(stringResult);

                }
            }
            catch (Exception e)
            {
                _logger.Error(e, e.Message);
                //CallOnDisconnected();
            }
            finally
            {
                _ws.Dispose();
            }
        }

        private void CallOnMessage(StringBuilder stringResult)
        {
            if (_onMessage != null)
                RunInTask(() => _onMessage(stringResult.ToString(), this));
        }

        private void CallOnDisconnected()
        {
            if (_onDisconnected != null)
                RunInTask(() => _onDisconnected(this));
        }

        private void CallOnConnected()
        {
            if (_onConnected != null)
                RunInTask(() => _onConnected(this));
        }

        private static void RunInTask(Action action)
        {
            Task.Factory.StartNew(action);
        }
    }
}
