using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Parse.LiveQuery;

/// <summary>
/// Interface for a WebSocket client supporting state changes, messages, and errors as observables.
/// </summary>
public interface IWebSocketClient
{
    IQbservable<WebSocketState> StateChanges { get; }
    IQbservable<string> Messages { get; }
    IQbservable<Exception> Errors { get; }

    void Open();
    void Close();
    Task Send(string message);
    WebSocketState State { get; }
}


/// <summary>
/// Represents the state of a WebSocket connection.
/// </summary>
public enum WebSocketState
{
    Open,
    Closed,
    Connecting,
    Error
}