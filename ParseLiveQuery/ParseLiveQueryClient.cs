using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

//using Parse.Common.Internal;
//using Parse.Core.Internal;
using static Parse.LiveQuery.LiveQueryException;
using System.Diagnostics;

namespace Parse.LiveQuery;

public class ParseLiveQueryClient
{

    private readonly Uri _hostUri;
    private readonly string _applicationId;
    private readonly string _clientKey;
    private readonly WebSocketClientFactory _webSocketClientFactory;
    private readonly IWebSocketClientCallback _webSocketClientCallback;
    private readonly ISubscriptionFactory _subscriptionFactory;
    private readonly ITaskQueue _taskQueue;

    private readonly ConcurrentDictionary<int, Subscription> _subscriptions = new ConcurrentDictionary<int, Subscription>();
    private readonly List<IParseLiveQueryClientCallbacks> _callbacks = new List<IParseLiveQueryClientCallbacks>();

    private IWebSocketClient _webSocketClient;
    private int _requestIdCount = 1;
    private bool _userInitiatedDisconnect;
    private bool _hasReceivedConnected;
    public ParseLiveQueryClient() : this(GetDefaultUri()) { }

    public ParseLiveQueryClient(Uri hostUri) : this(hostUri, WebSocketClient.Factory) { } // Updated Factory

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

        Uri serverUri = new Uri(server);
        return new UriBuilder(serverUri)
        {
            Scheme = serverUri.Scheme.Equals("https") ? "wss" : "ws"
        }.Uri;
    }

    /// <summary>
    /// Subscribes to a specified Parse query to receive real-time updates 
    /// for Create, Update, Delete, and other events on objects matching the query.
    /// </summary>
    /// <typeparam name="T">The type of ParseObject the query operates on.</typeparam>
    /// <param name="query">The Parse query to subscribe to.</param>
    /// <returns>
    /// A <see cref="Subscription{T}"/> instance that allows handling events like Create, Update, and Delete 
    /// on objects matching the query.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the client was explicitly disconnected and needs to be reconnected before subscribing.
    /// </exception>

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
                Debug.WriteLine("None...");
                Reconnect();
                break;
            case WebSocketClientState.Connecting:
                Debug.WriteLine("Connecting");
                break;
            case WebSocketClientState.Disconnecting:
                Debug.WriteLine("Disconnecting");
                Reconnect();
                break;
            case WebSocketClientState.Connected:
                Debug.WriteLine("Connected");
                //Reconnect();
                break;
            case WebSocketClientState.Disconnected:
                Debug.WriteLine("Disconnected");
                Reconnect();
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
                SendUnsubscription((Subscription<T>) sub);
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

    public void RegisterListener(IParseLiveQueryClientCallbacks listener)
    {
        _callbacks.Add(listener);
    }

    public void UnregisterListener(IParseLiveQueryClientCallbacks listener)
    {
        _callbacks.Add(listener);
    }

    // Private methods


    private WebSocketClientState GetWebSocketState()
    {
        var e = _webSocketClient?.State ?? WebSocketClientState.None;
        return e;
    }

    private bool IsConnected()
    {
        var e= _hasReceivedConnected && GetWebSocketState() == WebSocketClientState.Connected;
        return e;
    }


    private void SendSubscription(Subscription subscription)
    {
        _taskQueue.EnqueueOnError(
            SendOperationWithSessionAsync(subscription.CreateSubscribeClientOperation),
            error => subscription.DidEncounter(subscription.QueryObj, new UnknownException("Error when subscribing", error))
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

            // Convert based on JsonElement type
            object value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(), // Handle integers and floats
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element // Return as JsonElement if not a simple type
            };

            result[kvp.Key] = value;
        }

        return result;
    }

    private void ParseMessage(string message)
    {
        try
        {
            // Deserialize the message into a dictionary with JsonElement values
            var jsonElementDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(message);

            if (jsonElementDict == null || !jsonElementDict.ContainsKey("op"))
            {
                throw new InvalidResponseException("Message does not contain a valid 'op' field.");
            }

            // Convert JsonElement dictionary to object dictionary
            var jsonObject = ConvertJsonElements(jsonElementDict);

            // Extract the 'op' field
            string rawOperation = jsonObject["op"] as string;
            if (string.IsNullOrEmpty(rawOperation))
            {
                throw new InvalidResponseException("'op' field is null or empty.");
            }

            // Handle operations
            switch (rawOperation)
            {
                case "connected":
                    _hasReceivedConnected = true;
                    DispatchConnected();
                    foreach (Subscription subscription in _subscriptions.Values)
                    {
                        SendSubscription(subscription);
                    }
                    break;
                case "redirect":
                    // TODO: Handle redirect
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
                    throw new InvalidResponseException($"Unexpected operation: {rawOperation}");
            }
        }
        catch (Exception e) when (!(e is LiveQueryException))
        {
            throw new InvalidResponseException(message, e);
        }
    }


    private void DispatchConnected()
    {
        foreach (IParseLiveQueryClientCallbacks callback in _callbacks)
        {
            callback.OnLiveQueryClientConnected(this);
        }
    }

    private void DispatchDisconnected()
    {
        foreach (IParseLiveQueryClientCallbacks callback in _callbacks)
        {
            callback.OnLiveQueryClientDisconnected(this, _userInitiatedDisconnect);
        }
    }

    private void DispatchError(LiveQueryException exception)
    {
        foreach (IParseLiveQueryClientCallbacks callback in _callbacks)
        {
            callback.OnLiveQueryError(this, exception);
        }
    }

    private void DispatchSocketError(Exception exception)
    {
        _userInitiatedDisconnect = false;

        foreach (IParseLiveQueryClientCallbacks callback in _callbacks)
        {
            callback.OnSocketError(this, exception);
        }

        DispatchDisconnected();
    }


    private void HandleSubscribedEvent(IDictionary<string, object> jsonObject)
    {
        int requestId = Convert.ToInt32(jsonObject["requestId"]);

        Subscription subscription;
        if (_subscriptions.TryGetValue(requestId, out subscription))
        {
            subscription.DidSubscribe(subscription.QueryObj);
        }
    }

    private void HandleUnsubscribedEvent(IDictionary<string, object> jsonObject)
    {
        int requestId = Convert.ToInt32(jsonObject["requestId"]);

        Subscription subscription;
        if (_subscriptions.TryRemove(requestId, out subscription))
        {
            subscription.DidUnsubscribe(subscription.QueryObj);
        }
    }

    private void HandleObjectEvent(Subscription.Event subscriptionEvent, IDictionary<string, object> jsonObject)
    {
        try
        {
            int requestId = Convert.ToInt32(jsonObject["requestId"]);

            // Extract and process the 'object' field
            var objectElement = (JsonElement)jsonObject["object"];
            IDictionary<string, object> objectData = JsonElementToDictionary(objectElement);

            Subscription subscription;
            if (_subscriptions.TryGetValue(requestId, out subscription))
            {
                var objState = (IObjectState)ParseClient.Instance.Decoder.Decode(objectData, ParseClient.Instance.Services);
                subscription.DidReceive(subscription.QueryObj, subscriptionEvent, objState);
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
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return JsonElementToDictionary(element);
            case JsonValueKind.Array:
                var list = new List<object>();
                foreach (var arrayElement in element.EnumerateArray())
                {
                    list.Add(JsonElementToObject(arrayElement));
                }
                return list;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out long l) ? l : element.GetDouble();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();
            case JsonValueKind.Null:
                return null;
            default:
                throw new ArgumentException($"Unsupported JsonValueKind: {element.ValueKind}");
        }
    }


    private void HandleErrorEvent(IDictionary<string, object> jsonObject)
    {
        object requestId = jsonObject.GetOrDefault("requestId", null);
        int code = Convert.ToInt32(jsonObject["code"]);
        string error = (string)jsonObject["error"];
        bool reconnect = (bool)jsonObject["reconnect"];
        LiveQueryException exception = new ServerReportedException(code, error, reconnect);

        Subscription subscription;
        if (requestId != null && _subscriptions.TryGetValue(Convert.ToInt32(requestId), out subscription))
        {
            subscription.DidEncounter(subscription.QueryObj, exception);
        }
        DispatchError(exception);
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

                // Send the operation asynchronously and handle errors
                await _client.SendOperationWithSessionAsync(session =>
                    new ConnectClientOperation(_client._applicationId, _client._clientKey, session))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Use a flattened exception for better debugging
                var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                _client.DispatchError(exception.InnerException as LiveQueryException ??
                                      new UnknownException("Error connecting client", exception));
            }
        }

        public async void OnMessage(string message)
        {
            try
            {
                // Process the message asynchronously and handle errors
                await _client.HandleOperationAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Use a flattened exception for better debugging
                var exception = ex is AggregateException ae ? ae.Flatten() : ex;
                _client.DispatchError(exception.InnerException as LiveQueryException ??
                                      new UnknownException($"Error handling message: {message}", exception));
            }
        }


        public void OnClose()
        {
            _client._hasReceivedConnected = false;
            _client.DispatchDisconnected();
        }

        public void OnError(Exception exception)
        {
            _client._hasReceivedConnected = false;
            _client.DispatchSocketError(exception);
        }

        public void OnStateChanged()
        {
            // do nothing or maybe TODO logging
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
        private readonly TaskQueue _underlying = new(); // Simplified initialization

        public async Task Enqueue(Action taskStart)
        {
            await _underlying.Enqueue(async _ =>
            {
                taskStart();
                await Task.CompletedTask; // Ensures compatibility with async API
            }, CancellationToken.None);
        }

        public async Task EnqueueOnSuccess<TIn>(Task<TIn> task, Func<Task<TIn>, Task> onSuccess)
        {
            try
            {
                await task.ConfigureAwait(false); // Await the original task
                await onSuccess(task).ConfigureAwait(false); // Pass to the success continuation
            }
            catch (Exception ex)
            {
                // Ensure exceptions propagate if necessary
                throw new InvalidOperationException("Error in EnqueueOnSuccess", ex);
            }
        }

        public async Task EnqueueOnError(Task task, Action<Exception> onError)
        {
            try
            {
                await task.ConfigureAwait(false); // Await the original task
            }
            catch (Exception ex)
            {
                onError(ex); // Call error handler
            }
        }
    }

}
