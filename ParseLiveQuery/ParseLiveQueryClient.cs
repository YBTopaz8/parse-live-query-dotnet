
using System.Reactive.Linq;      
using System.Reactive.Subjects;    

using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;

namespace Parse.LiveQuery
{
    public class ParseLiveQueryClient
    {
        private readonly Uri _hostUri;
        private readonly string _applicationId;
        private readonly string _clientKey;
        private readonly WebSocketClientFactory _webSocketClientFactory;
        private readonly IWebSocketClientCallback _webSocketClientCallback;
        private readonly ISubscriptionFactory _subscriptionFactory;
        private readonly ITaskQueue _taskQueue;

        private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new();

        private IWebSocketClient _webSocketClient;
        private int _requestIdCount = 1;
        private bool _userInitiatedDisconnect;
        private bool _hasReceivedConnected;

        private readonly Subject<ParseLiveQueryClient> _connectedSubject = new();               // ADDED
        private readonly Subject<(ParseLiveQueryClient client, bool userInitiated)> _disconnectedSubject = new(); // ADDED
        private readonly Subject<LiveQueryException> _errorSubject = new();                       // ADDED
        private readonly Subject<(int requestId, Subscription subscription)> _subscribedSubject = new();       // ADDED
        private readonly Subject<(int requestId, Subscription subscription)> _unsubscribedSubject = new();     // ADDED
        private readonly Subject<(Subscription.Event evt, object objectDictionnary, Subscription subscription)> _objectEventSubject = new(); // ADDED

        public IObservable<ParseLiveQueryClient> OnConnected => _connectedSubject.AsObservable();                          // ADDED
        public IObservable<(ParseLiveQueryClient client, bool userInitiated)> OnDisconnected => _disconnectedSubject.AsObservable(); // ADDED
        public IObservable<LiveQueryException> OnError => _errorSubject.AsObservable();                                    // ADDED
        public IObservable<(int requestId, Subscription subscription)> OnSubscribed => _subscribedSubject.AsObservable();   // ADDED
        public IObservable<(int requestId, Subscription subscription)> OnUnsubscribed => _unsubscribedSubject.AsObservable(); // ADDED
        public IObservable<(Subscription.Event evt, object objectDictionnary, Subscription subscription)> OnObjectEvent => _objectEventSubject.AsObservable(); // ADDED


        public ParseLiveQueryClient() : this(GetDefaultUri()) { }
        public ParseLiveQueryClient(Uri hostUri) : this(hostUri, WebSocketClient.Factory) { } // Updated Factory, no callback changes here
        public ParseLiveQueryClient(WebSocketClientFactory webSocketClientFactory) : this(GetDefaultUri(), webSocketClientFactory) { }
        public ParseLiveQueryClient(Uri hostUri, WebSocketClientFactory webSocketClientFactory) :
            this(hostUri, webSocketClientFactory, new SubscriptionFactory(), new TaskQueueWrapper())
        { }

        internal ParseLiveQueryClient(Uri hostUri, WebSocketClientFactory webSocketClientFactory,
            ISubscriptionFactory subscriptionFactory, ITaskQueue taskQueue)
        {
            _hostUri = hostUri;
            _applicationId = ParseClient.Instance.ServerConnectionData.ApplicationID;
            _clientKey = ParseClient.Instance.ServerConnectionData.Key;

            _webSocketClientFactory = webSocketClientFactory;
            _webSocketClientCallback = new WebSocketClientCallback(this);
            _subscriptionFactory = subscriptionFactory;
            _taskQueue = taskQueue;
        }

        private static Uri GetDefaultUri()
        {
            string server = ParseClient.Instance.ServerConnectionData.ServerURI;
            if (server == null)
                throw new InvalidOperationException("Missing default Server URI in CurrentConfiguration");

            Uri serverUri = new (server);
            return new UriBuilder(serverUri)
            {
                Scheme = serverUri.Scheme.Equals("https") ? "wss" : "ws"
            }.Uri;
        }

        /// <summary>
        /// Subscribes to a specified Parse query to receive real-time updates 
        /// for Create, Update, Delete, and other events on objects matching the query.
        /// </summary>
        public Subscription<T> Subscribe<T>(ParseQuery<T> query) where T : ParseObject
        {
            int requestId = _requestIdCount++;
            Subscription<T> subscription = _subscriptionFactory.CreateSubscription(requestId, query);

            _subscriptions.TryAdd(requestId, subscription);

            if (IsConnected())
            {
                SendSubscription(subscription);
            }
            else if (_userInitiatedDisconnect)
            {
                throw new InvalidOperationException("The client was explicitly disconnected and must be reconnected before subscribing");
            }
            else
            {
                ConnectIfNeeded();
            }
            return subscription;
        }

        public void ConnectIfNeeded()
        {
            switch (GetWebSocketState())
            {
                case WebSocketClientState.None:
                    
                    Reconnect();
                    break;
                case WebSocketClientState.Connecting:
                    
                    break;
                case WebSocketClientState.Disconnecting:
                    
                    Reconnect();
                    break;
                case WebSocketClientState.Connected:
                    
                    break;
                case WebSocketClientState.Disconnected:
                    
                    Reconnect();
                    break;
                default:
                    break;
            }
        }

        public void Unsubscribe<T>(ParseQuery<T> query) where T : ParseObject
        {
            if (query == null)
            {
                return;
            }
            var requestIds = new List<int>();
            foreach (int rId in _subscriptions.Keys)
            {
                var sub = _subscriptions[rId];
                if (query.Equals(sub.QueryObj))
                {
                    SendUnsubscription((Subscription<T>)sub);
                    requestIds.Add(rId);
                }
            }

            Subscription dummy = null;
            foreach (int requestId in requestIds)
            {
                _subscriptions.TryRemove(requestId, out dummy);
            }
        }

        public void Unsubscribe<T>(ParseQuery<T> query, Subscription<T> subscription) where T : ParseObject
        {
            if (query == null || subscription == null)
                return;
            var requestIds = new List<int>();
            foreach (int requestId in _subscriptions.Keys)
            {
                var sub = _subscriptions[requestId];
                if (query.Equals(sub.QueryObj) && subscription.Equals(sub))
                {
                    SendUnsubscription(subscription);
                    requestIds.Add(requestId);
                }
            }
            Subscription dummy = null;
            foreach (int requestId in requestIds)
            {
                _subscriptions.TryRemove(requestId, out dummy);
            }
        }

        public void Reconnect()
        {
            _webSocketClient?.Close();
            _userInitiatedDisconnect = false;
            _hasReceivedConnected = false;
            _webSocketClient = _webSocketClientFactory(_hostUri, _webSocketClientCallback);
            _webSocketClient.Open();
        }

        public void Disconnect()
        {
            _webSocketClient?.Close();
            _webSocketClient = null;

            _userInitiatedDisconnect = true;
            _hasReceivedConnected = false;
        }

        private WebSocketClientState GetWebSocketState()
        {
            if (_webSocketClient == null)
                return WebSocketClientState.None;

            var wsState = _webSocketClient.State;

            switch (wsState)
            {
                case WebSocketState.Connecting:
                    return WebSocketClientState.Connecting;
                case WebSocketState.Open:
                    return WebSocketClientState.Connected;
                case WebSocketState.Closed:
                case WebSocketState.Error:
                    return WebSocketClientState.Disconnected;
                default:
                    return WebSocketClientState.None;
            }
        }


        private bool IsConnected()
        {
            var wsState = GetWebSocketState();
            if (wsState == WebSocketClientState.Connected)
            {

            }
            var e = _hasReceivedConnected;
            return e;
        }

        private void SendSubscription(Subscription subscription)
        {
            _taskQueue.EnqueueOnError(
                SendOperationWithSessionAsync(subscription.CreateSubscribeClientOperation),
                error => subscription.DidEncounter(subscription.QueryObj, new LiveQueryException.UnknownException("Error when subscribing", error))
            );
        }

        private void SendUnsubscription<T>(Subscription<T> subscription) where T : ParseObject
        {
            SendOperationAsync(new UnsubscribeClientOperation(subscription.RequestId));
        }

        private Task SendOperationWithSessionAsync(Func<string, IClientOperation> operationFunc)
        {
            return _taskQueue.EnqueueOnSuccess(
                ParseClient.Instance.CurrentUserController.GetCurrentSessionTokenAsync(ParseClient.Instance.Services, CancellationToken.None),
                currentSessionTokenTask => SendOperationAsync(operationFunc(currentSessionTokenTask.Result))
            );
        }

        private Task SendOperationAsync(IClientOperation operation)
        {
            return _taskQueue.Enqueue(() => _webSocketClient.Send(operation.ToJson()));
        }

        private Task HandleOperationAsync(string message)
        {
            return _taskQueue.Enqueue(() => ParseMessage(message));
        }

        private IDictionary<string, object> ConvertJsonElements(Dictionary<string, JsonElement> jsonElementDict)
        {
            var result = new Dictionary<string, object>();

            foreach (var kvp in jsonElementDict)
            {
                JsonElement element = kvp.Value;

                object value = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => element
                };

                result[kvp.Key] = value;
            }

            return result;
        }

        private void ParseMessage(string message)
        {
            try
            {
                var jsonElementDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);

                if (jsonElementDict == null || !jsonElementDict.ContainsKey("op"))
                {
                    throw new LiveQueryException.InvalidResponseException("Message does not contain a valid 'op' field.");
                }

                var jsonObject = ConvertJsonElements(jsonElementDict);
                string rawOperation = jsonObject["op"] as string;
                if (string.IsNullOrEmpty(rawOperation))
                {
                    throw new LiveQueryException.InvalidResponseException("'op' field is null or empty.");
                }

                switch (rawOperation)
                {
                    case "connected":
                        _hasReceivedConnected = true;
                        // CHANGED: Instead of DispatchConnected(), we now use Rx subject
                        // DispatchConnected(); // REMOVED
                        _connectedSubject.OnNext(this); // ADDED
                        foreach (Subscription subscription in _subscriptions.Values)
                        {
                            SendSubscription(subscription);
                        }
                        break;
                    case "redirect":
                        // TODO: Handle redirect if needed
                        break;
                    case "subscribed":
                        HandleSubscribedEvent(jsonObject);
                        break;
                    case "unsubscribed":
                        HandleUnsubscribedEvent(jsonObject);
                        break;
                    case "enter":
                        HandleObjectEvent(Subscription.Event.Enter, jsonObject);
                        break;
                    case "leave":
                        HandleObjectEvent(Subscription.Event.Leave, jsonObject);
                        break;
                    case "update":
                        HandleObjectEvent(Subscription.Event.Update, jsonObject);
                        break;
                    case "create":
                        HandleObjectEvent(Subscription.Event.Create, jsonObject);
                        break;
                    case "delete":
                        HandleObjectEvent(Subscription.Event.Delete, jsonObject);
                        break;
                    case "error":
                        HandleErrorEvent(jsonObject);
                        break;
                    default:
                        throw new LiveQueryException.InvalidResponseException($"Unexpected operation: {rawOperation}");
                }
            }
            catch (Exception e) when (!(e is LiveQueryException))
            {
                throw new LiveQueryException.InvalidResponseException(message, e);
            }
        }

        // CHANGED: Removed DispatchConnected/Disconnected/Error/SocketError with callback loops
        // They are replaced with Subject.OnNext calls directly

        private void HandleSubscribedEvent(IDictionary<string, object> jsonObject)
        {
            int requestId = Convert.ToInt32(jsonObject["requestId"]);

            if (_subscriptions.TryGetValue(requestId, out var subscription))
            {
                subscription.DidSubscribe(subscription.QueryObj);
                // CHANGED: Instead of callback-based dispatch, use Rx subject
                _subscribedSubject.OnNext((requestId, subscription)); // ADDED
            }
        }

        private void HandleUnsubscribedEvent(IDictionary<string, object> jsonObject)
        {
            int requestId = Convert.ToInt32(jsonObject["requestId"]);

            if (_subscriptions.TryRemove(requestId, out var subscription))
            {
                subscription.DidUnsubscribe(subscription.QueryObj);
                // CHANGED: Instead of callback-based dispatch, use Rx subject
                _unsubscribedSubject.OnNext((requestId, subscription)); // ADDED
            }
        }

        private void HandleObjectEvent(Subscription.Event subscriptionEvent, IDictionary<string, object> jsonObject)
        {
            try
            {
                int requestId = Convert.ToInt32(jsonObject["requestId"]);
                var objectElement = (JsonElement)jsonObject["object"];
                //var objectElement = (JsonElement)jsonObject["original"]; // TODO: Expose this in the future
                IDictionary<string, object> objectData = JsonElementToDictionary(objectElement);

                if (_subscriptions.TryGetValue(requestId, out var subscription))
                {
                    var obj = ParseClient.Instance.Decoder.Decode(objectData, ParseClient.Instance.Services);
        
                    //TODO 
                    _objectEventSubject.OnNext((subscriptionEvent, obj, subscription)); // ADDED
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HandleObjectEvent: {ex.Message}");
            }
        }

        private IDictionary<string, object> JsonElementToDictionary(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Expected JsonElement to be an object.");
            }

            var result = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = JsonElementToObject(property.Value);
            }

            return result;
        }

        private object JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => JsonElementToDictionary(element),
                JsonValueKind.Array => JsonArrayToObjectList(element),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => throw new ArgumentException($"Unsupported JsonValueKind: {element.ValueKind}")
            };
        }

        private List<object> JsonArrayToObjectList(JsonElement element)
        {
            var list = new List<object>();
            foreach (var arrayElement in element.EnumerateArray())
            {
                list.Add(JsonElementToObject(arrayElement));
            }
            return list;
        }

        private void HandleErrorEvent(IDictionary<string, object> jsonObject)
        {
            object requestId = jsonObject.GetOrDefault("requestId", null);
            int code = Convert.ToInt32(jsonObject["code"]);
            string error = (string)jsonObject["error"];
            bool reconnect = (bool)jsonObject["reconnect"];
            LiveQueryException exception = new LiveQueryException.ServerReportedException(code, error, reconnect);

            if (requestId != null && _subscriptions.TryGetValue(Convert.ToInt32(requestId), out var subscription))
            {
                subscription.DidEncounter(subscription.QueryObj, exception);
            }
            _errorSubject.OnNext(exception); // ADDED
        }

        private void OnWebSocketClosed()
        {
            _disconnectedSubject.OnNext((this, _userInitiatedDisconnect)); // ADDED
        }

        private void OnWebSocketError(Exception exception)
        {
            _errorSubject.OnNext(new LiveQueryException.UnknownException("Socket error", exception));
            _disconnectedSubject.OnNext((this, _userInitiatedDisconnect)); // ADDED
        }

        private class WebSocketClientCallback : IWebSocketClientCallback
        {
            private readonly ParseLiveQueryClient _client;

            public WebSocketClientCallback(ParseLiveQueryClient client)
            {
                _client = client;
            }

            public async void OnOpen()
            {
                try
                {
                    _client._hasReceivedConnected = false;
                    await _client.SendOperationWithSessionAsync(session =>
                        new ConnectClientOperation(_client._applicationId, _client._clientKey, session))
                        .ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                    // CHANGED: Instead of DispatchError, use Rx subject
                    _client._errorSubject.OnNext(
                        exception is LiveQueryException lqex ? lqex :
                        new LiveQueryException.UnknownException("Error connecting client", exception)); // ADDED
                }
            }

            public async void OnMessage(string message)
            {
                try
                {
                    await _client.HandleOperationAsync(message).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                    // CHANGED: Instead of DispatchError, use Rx subject
                    _client._errorSubject.OnNext(
                        exception is LiveQueryException lqex ? lqex :
                        new LiveQueryException.UnknownException($"Error handling message: {message}", exception)); // ADDED
                }
            }

            public void OnClose()
            {
                _client._hasReceivedConnected = false;
                _client.OnWebSocketClosed(); // ADDED call to new method that triggers Rx disconnected event
            }

            public void OnError(Exception exception)
            {
                _client._hasReceivedConnected = false;
                _client.OnWebSocketError(exception); // ADDED call to new method that triggers Rx error event
            }

            public void OnStateChanged()
            {
                // No callback needed
            }
        }

        private class SubscriptionFactory : ISubscriptionFactory
        {
            public Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query) where T : ParseObject
            {
                return new Subscription<T>(requestId, query);
            }
        }

        private class TaskQueueWrapper : ITaskQueue
        {
            private readonly TaskQueue _underlying = new();

            public async Task Enqueue(Action taskStart)
            {
                await _underlying.Enqueue(async _ =>
                {
                    taskStart();
                    await Task.CompletedTask;
                }, CancellationToken.None);
            }

            public async Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess)
            {
                try
                {
                    await task.ConfigureAwait(false);
                    await onSuccess(task).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Error in EnqueueOnSuccess", ex);
                }
            }

            public async Task EnqueueOnError(Task task, Action<Exception> onError)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
            }
        }

    }


}
public interface IWebSocketClientCallback
{
    void OnOpen();
    void OnMessage(string message);
    void OnClose();
    void OnError(Exception exception);
    void OnStateChanged();
}