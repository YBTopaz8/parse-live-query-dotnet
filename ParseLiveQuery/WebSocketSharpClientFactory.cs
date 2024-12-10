using System;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

public class WebSocketClient : IWebSocketClient
{
    public static readonly WebSocketClientFactory Factory = (hostUri, callback) => new WebSocketClient(hostUri, callback);


    private readonly Uri _hostUri;
    private readonly IWebSocketClientCallback _webSocketClientCallback;
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;

    private readonly Subject<WebSocketState> _stateChanges = new(); 
    private WebSocketState _currentState;

    private readonly Subject<string> _messages = new();
    private readonly Subject<Exception> _errors = new();

    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback)
    {
        _hostUri = hostUri;
        _webSocketClientCallback = webSocketClientCallback;
        _webSocket = new ClientWebSocket();        
    }
    public IQbservable<WebSocketState> StateChanges => _stateChanges.AsQbservable();
    public IQbservable<string> Messages => _messages.AsQbservable();
    public IQbservable<Exception> Errors => _errors.AsQbservable();

    public WebSocketState State { get; private set; }


    public async void Open()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        _webSocket = new ClientWebSocket();
        _stateChanges.OnNext(WebSocketState.Connecting);
        
        try
        {
            await _webSocket.ConnectAsync(_hostUri, _cancellationTokenSource.Token);
            _stateChanges.OnNext(WebSocketState.Open);
            _webSocketClientCallback.OnOpen();
            _ = ReceiveLoopAsync(); // Start receiving messages
        }
        catch (Exception ex)
        {
            _webSocketClientCallback.OnError(ex);
            _errors.OnNext(ex);
            _stateChanges.OnNext(WebSocketState.Error);
        }
    }


    public async void Close()
    {
        try
        {
            _stateChanges.OnNext(WebSocketState.Closed);
            _cancellationTokenSource.Cancel();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
        }
        finally
        {
            _webSocketClientCallback.OnClose();
        }
}
    public async Task Send(string message)
    {
        if (_webSocket.State != System.Net.WebSockets.WebSocketState.Open)
        {
            var exception = new InvalidOperationException("WebSocket is not connected.");
            _errors.OnNext(exception);
            _webSocketClientCallback.OnError(exception);
            return;
        }
        try
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);
            await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
            _webSocketClientCallback.OnError(ex);
        }
    }
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        while (_webSocket.State == System.Net.WebSockets.WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _stateChanges.OnNext(WebSocketState.Closed);
                    _webSocketClientCallback.OnClose();
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _messages.OnNext(message);
                _webSocketClientCallback.OnMessage(message);
            }
            catch (Exception ex)
            {
                _errors.OnNext(ex);
                _webSocketClientCallback.OnError(ex);
            }
        }
    }
    private void SetState(WebSocketState state)
    {
        State = state;
        _stateChanges.OnNext(state);
    }
}
