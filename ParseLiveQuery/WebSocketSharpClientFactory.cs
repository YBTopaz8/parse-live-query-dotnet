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
    public static readonly WebSocketClientFactory Factory = (hostUri) => new WebSocketClient(hostUri);


    private readonly Uri _hostUri;
    private readonly ClientWebSocket _webSocket;
    private readonly CancellationTokenSource _cts;

    private readonly Subject<WebSocketState> _stateChanges = new();
    private readonly Subject<string> _messages = new();
    private readonly Subject<Exception> _errors = new();

    public WebSocketClient(Uri hostUri)
    {
        _hostUri = hostUri;
        _webSocket = new ClientWebSocket();
        _cts = new CancellationTokenSource();
    }

    public IQbservable<WebSocketState> StateChanges => _stateChanges.AsQbservable();
    public IQbservable<string> Messages => _messages.AsQbservable();
    public IQbservable<Exception> Errors => _errors.AsQbservable();

    public WebSocketState State => throw new NotImplementedException();

    public async void Open()
    {
        try
        {
            _stateChanges.OnNext(WebSocketState.Connecting);
            await _webSocket.ConnectAsync(_hostUri, _cts.Token);
            _stateChanges.OnNext(WebSocketState.Open);
            _ = ReceiveLoopAsync(); // Start receiving messages
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
            _stateChanges.OnNext(WebSocketState.Error);
        }
    }


    public async void Close()
    {
        try
        {
            _stateChanges.OnNext(WebSocketState.Closed);
            _cts.Cancel();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by user", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
        }
    }
    public async Task Send(string message)
    {
        try
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);
            await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, _cts.Token);
        }
        catch (Exception ex)
        {
            _errors.OnNext(ex);
        }
    }
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        while (_webSocket.State == System.Net.WebSockets.WebSocketState.Open && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _stateChanges.OnNext(WebSocketState.Closed);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _messages.OnNext(message);
            }
            catch (Exception ex)
            {
                _errors.OnNext(ex);
            }
        }
    }

}
