# Parse-Live-Query-Unofficial v2 (with RX and LINQ Support)

**Key Changes from v1:**  
- Now supports .NET 5, 6, 7, 8, and .NET MAUI.  
- Uses `System.Net.WebSockets.Client` for better connectivity.  
- Replaced callbacks (CB) with Reactive Extensions (Rx.NET) for event handling.  
- Full LINQ support integrated.  
- Enhanced stability and MAUI compatibility.

**Thanks :)** to:
- [JonMcPherson](https://github.com/JonMcPherson/parse-live-query-dotnet) for original Parse Live Query dotnet code.  
- [Parse Community](https://github.com/parse-community) for Parse SDK.  
- My [Parse-SDK fork](https://github.com/YBTopaz8/Parse-SDK-dotNET) which this depends on.  

## What is Live Query?
Live Query provides real-time data sync between server and clients over WebSockets. When data changes on the server, subscribed clients are instantly notified.

## Prerequisites
- Ensure Live Query is enabled for your classes in the Parse Dashboard.

Example in Back4App:  
![Enable LQ](https://github.com/user-attachments/assets/b9cba805-f81a-47e2-a999-ce6864ba438a)

## Basic Setup

### 1. Initialization
```csharp
using Parse; // Parse
using Parse.LiveQuery; // ParseLQ
using System.Reactive.Linq; // For Rx
using System.Linq; // For LINQ
using System.Collections.ObjectModel;

// Check internet
if (Connectivity.NetworkAccess != NetworkAccess.Internet)
{
    Console.WriteLine("No Internet, can't init ParseClient.");
    return;
}

// Init ParseClient
var client = new ParseClient(new ServerConnectionData
{
    ApplicationID = "YOUR_APP_ID",
    ServerURI = new Uri("YOUR_SERVER_URL"),
    Key = "YOUR_CLIENT_KEY" // or MasterKey
}, new HostManifestData
{
    Version = "1.0.0",
    Identifier = "com.yourcompany.yourmauiapp",
    Name = "MyMauiApp"
});

client.Publicize(); // Access via ParseClient.Instance globally
```

### 2. Simple Subscription Setup
```csharp

    //I Will Just leave all this code in Docs because believe it or not, sometimes even I forget how to use my own lib :D
    void SetupLiveQuery()
    {
        try
        {
            var query = ParseClient.Instance.GetQuery("TestChat");
            var subscription = LiveClient.Subscribe(query);

            LiveClient.ConnectIfNeeded();

            // Rx event streams
            LiveClient.OnConnected
                .Subscribe(_ => Debug.WriteLine("LiveQuery connected."));
            LiveClient.OnDisconnected
                .Subscribe(info => Debug.WriteLine(info.userInitiated
                    ? "User disconnected."
                    : "Server disconnected."));
            LiveClient.OnError
                .Subscribe(ex => Debug.WriteLine("LQ Error: " + ex.Message));
            LiveClient.OnSubscribed
                .Subscribe(e => Debug.WriteLine("Subscribed to: " + e.requestId));

            // Handle object events (Create/Update/Delete)
            LiveClient.OnObjectEvent
                .Where(e => e.subscription == subscription)
                
                .Subscribe(e =>
                {
                    Debug.WriteLine($"Message before {Message?.Length}");
                    TestChat chat = new();
                    var objData = (e.objectDictionnary as Dictionary<string, object>);

                    switch (e.evt)
                    {
                        
                        case Subscription.Event.Enter:
                            Debug.WriteLine("entered");
                            break;
                        case Subscription.Event.Leave:
                            Debug.WriteLine("Left");
                            break;
                            case Subscription.Event.Create:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            Messages.Add(chat);
                            break;
                            case Subscription.Event.Update:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            var obj = Messages.FirstOrDefault(x => x.UniqueKey == chat.UniqueKey);
                            
                            Messages.RemoveAt(Messages.IndexOf(obj));
                            Messages.Add(chat);

                            break;
                            case Subscription.Event.Delete:
                            chat = ObjectMapper.MapFromDictionary<TestChat>(objData);
                            var objj = Messages.FirstOrDefault(x => x.UniqueKey == chat.UniqueKey);

                            Messages.RemoveAt(Messages.IndexOf(objj));
                            break;
                        default:
                            break;
                    }
                    Debug.WriteLine($"Message after {Message.Length}");
                    Debug.WriteLine($"Event {e.evt} on object {e.objectDictionnary.GetType()}");
                });

        }
        catch (Exception ex)
        {
            Debug.WriteLine("SetupLiveQuery Error: " + ex.Message);
        }
    }
```
## Little Helper Method :)

```


public static class ObjectMapper
{
    /// <summary>
    /// Maps values from a dictionary to an instance of type T.
    /// Logs any keys that don't match properties in T.
    ///     
    /// Helper to Map from Parse Dictionnary Response to Model
    /// Example usage TestChat chat = ObjectMapper.MapFromDictionary<TestChat>(objData);    
    /// </summary>
    public static T MapFromDictionary<T>(IDictionary<string, object> source) where T : new()
    {
        // Create an instance of T
        T target = new T();

        // Get all writable properties of T
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

        // Track unmatched keys
        List<string> unmatchedKeys = new();

        foreach (var kvp in source)
        {
            if (properties.TryGetValue(kvp.Key, out var property))
            {
                try
                {
                    // Convert and assign the value to the property
                    if (kvp.Value != null && property.PropertyType.IsAssignableFrom(kvp.Value.GetType()))
                    {
                        property.SetValue(target, kvp.Value);
                    }
                    else if (kvp.Value != null)
                    {
                        // Attempt conversion for non-directly assignable types
                        var convertedValue = Convert.ChangeType(kvp.Value, property.PropertyType);
                        property.SetValue(target, convertedValue);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to set property {property.Name}: {ex.Message}");
                }
            }
            else
            {
                // Log unmatched keys
                unmatchedKeys.Add(kvp.Key);
            }
        }

        // Log keys that don't match
        if (unmatchedKeys.Count > 0)
        {
            Debug.WriteLine("Unmatched Keys:");
            foreach (var key in unmatchedKeys)
            {
                Debug.WriteLine($"- {key}");
            }
        }

        return target;
    }
}

```


## Conclusion
This v2 version integrates LINQ and Rx.NET, enabling highly flexible and reactive real-time data flows with Parse Live Queries. Advanced filtering, buffering, throttling, and complex transformations are now possible with minimal code.

PRs are welcome!
