using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

public class WebSocketClient : IWebSocketClient
{
    public static readonly WebSocketClientFactory Factory = (hostUri, callback) => new WebSocketClient(hostUri, callback);

    private readonly object _mutex = new object();
    private readonly Uri _hostUri;
    private readonly IWebSocketClientCallback _webSocketClientCallback;

    private volatile WebSocketClientState _state = WebSocketClientState.None;
    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cancellationTokenSource;

    public WebSocketClient(Uri hostUri, IWebSocketClientCallback webSocketClientCallback)
    {
        _hostUri = hostUri;
        _webSocketClientCallback = webSocketClientCallback;
    }

    public WebSocketClientState State => _state;

    public async Task Open()
    {
        await SynchronizeWhen(WebSocketClientState.None, async () =>
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            _state = WebSocketClientState.Connecting;

            try
            {
                await _webSocket.ConnectAsync(_hostUri, _cancellationTokenSource.Token);
                _state = WebSocketClientState.Connected;
                _webSocketClientCallback.OnOpen();

                _ = StartReceivingAsync(); // Start receiving messages
            }
            catch (Exception ex)
            {
                _state = WebSocketClientState.Disconnected;
                _webSocketClientCallback.OnError(ex);
            }
        });
    }

    public async Task Close()
    {
        await SynchronizeWhenNot(WebSocketClientState.None, async () =>
        {
            _state = WebSocketClientState.Disconnecting;

            try
            {
                _cancellationTokenSource?.Cancel();
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "User invoked close", CancellationToken.None);
            }
            catch (Exception ex)
            {
                _webSocketClientCallback.OnError(ex);
            }
            finally
            {
                _state = WebSocketClientState.Disconnected;
                _webSocketClientCallback.OnClose();
            }
        });
    }

    public async Task Send(string message)
    {
        await SynchronizeWhen(WebSocketClientState.Connected, async () =>
        {
            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                var segment = new ArraySegment<byte>(buffer);
                await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                
            }
            catch (Exception ex)
            {
                _webSocketClientCallback.OnError(ex);
            }
        });
    }

    private async Task StartReceivingAsync()
    {
        var buffer = new ArraySegment<byte>(new byte[8192]);

        try
        {
            while (_state == WebSocketClientState.Connected && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await _webSocket.ReceiveAsync(buffer, _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _state = WebSocketClientState.Disconnected;
                    _webSocketClientCallback.OnClose();
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                _webSocketClientCallback.OnMessage(message);
            }
        }
        catch (Exception ex)
        {
            _webSocketClientCallback.OnError(ex);
            _state = WebSocketClientState.Disconnected;
        }
    }

    // Synchronization Helpers

    private async Task SynchronizeAsync(Func<Task> action, Func<bool> predicate = null)
    {
        predicate ??= () => true; // Default predicate ensures it's never null

        bool stateChanged = false;
        lock (_mutex)
        {
            var previousState = _state;
            if (predicate())
            {
                stateChanged = true; // State is allowed to change
            }
        }

        // Perform actions outside of the lock to avoid deadlocks
        if (stateChanged)
        {
            await action().ConfigureAwait(false); // Ensure proper task continuation without capturing context
            _webSocketClientCallback.OnStateChanged();
        }
    }


    private async Task SynchronizeWhen(WebSocketClientState state, Func<Task> action)
    {
        await SynchronizeAsync(action, () => _state == state);
    }

    private async Task SynchronizeWhenNot(WebSocketClientState state, Func<Task> action)
    {
        await SynchronizeAsync(action, () => _state != state);
    }
}