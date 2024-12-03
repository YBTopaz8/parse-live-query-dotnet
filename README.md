### Parse-Live-Query-Unofficial
- Added Support for .NET 9 and .NET MAUI.
- Replaced previous web client with `System.Net.WebSockets.Client` as I believe is better.
- Should be FULLY compatible with .NET 9 and MAUI.


Here is a full YouTube video walking through what it is, and how to use it.

[https://youtu.be/OlpHIJDvl7E](https://youtu.be/V-cUjq7Js84)


Here is the full ReadMe;

Since I Updated this based on my MAUI Projects, I had to update my fork of Parse SDK to have MAUI support, thus 
## THIS is DEPENDENT on my [ParseSDK fork](https://github.com/YBTopaz8/Parse-SDK-dotNET). 

Will release a Nuget version later for both.

### What are Live Queries?
Glad you asked!
So to put it VERY SIMPLE : Live Queries lets you build a **RealTime Chat App** in less than idk 25 lines of code? (Excluding UI ofc).

In "Long" it essentially opens a **Websocket/caller-listener tunnel** where _WHATEVER_ changes done to the subscribed class/table(for sql folks), will reflect to ALL listening clients in "real time" (very very minimal delay. Like and Eye blink's delay!)

Example: You Create a Class/Table (for SQL folks) then tell the server "Whatever is done here _TELL EVERYONE WHO NEEDS TO BE INFORMED ASAP_ ", then the client apps (your developed app) will subscribe to the _EVENTUAL POSSIBILITIES_ of any change happening to your class/table records.

As such. 
1. Device A Subscribes to class/table "Comments" found in Server.
2. Device B subscribes to "Comments" too.
3. Device A -> Server (A sends to server)
4. Device A <- Server -> Device B (Server sends back data to both devices _ASSUMING THEY ARE ALLOWED TO VIEW THE CHANGES_, you can configure this with ACL!)

I hope it's a OVERexplanation now haha! But if you any more specific questions, please shoot!

How To Use In addition to the docs over from the folks at ;
For now:

### STEP 0 (VERY IMPORTANT): MAKE SURE YOUR ENABLE LIVE QUERY ON THE CLASSES NEEDED.
I used Back4Apps for dev/testing so the 2 classes I needed were checked!
![image](https://github.com/user-attachments/assets/b9cba805-f81a-47e2-a999-ce6864ba438a)


### 1. Download the project's source and add to your solution , your solution will look like this
![image](https://github.com/user-attachments/assets/94a9b76d-20bb-4ec0-9fb6-ede63767a608)
### 2. You will have to initialize your ParseClient with your app's details
```
// Check for internet connection
if (Connectivity.NetworkAccess != NetworkAccess.Internet)
{
    Console.WriteLine("No Internet Connection: Unable to initialize ParseClient.");
    return false;
}

// Create ParseClient
ParseClient client = new ParseClient(new ServerConnectionData
{
    ApplicationID = APIKeys.ApplicationId, 
    ServerURI = APIKeys.ServerUri,
    Key = APIKeys.DotNetKEY, // Or use MASTERKEY

}
);
HostManifestData manifest = new HostManifestData() // Optional but I really recommend setting just to avoid potential crashes
{
    Version = "1.0.0",
    Identifier = "com.yvanbrunel.myMauiApp",
    Name = "myAppName",
};

client.Publicize(); // Best to use if you want to access ParseClient.Instance anywhere in the app.
```
### 3. Setup Live Queries

A subscription can be very easily handled as such;
```
ParseLiveQueryClient LiveClient = new ParseLiveQueryClient();
 async Task SetupLiveQuery()
 {
     try
     {
         var query = ParseClient.Instance.GetQuery("CLASS_TO_SUB_TO");
         //.WhereEqualTo("IsDeleted", false); Condition where App/Server will communicate

         var subscription = LiveClient.Subscribe(query); // You subscribe here
        
//optional
 LiveClient.RegisterListener(this); // This is important is you need to reach to Web Connection changes


         sub.HandleSubscribe(async query =>
         {
             await Shell.Current.DisplayAlert("Subscribed", "Subscribed to query", "OK");
             Debug.WriteLine($"Subscribed to query: {query.GetClassName()}"); 
         })
         .HandleEvents((query, objEvent, obj) =>
         {
             var object = obj  // will be the data returned by server. Could be new data, Same data or no data (After a Create, Read/Update/ Delete respectively)

             if (objEvent == Subscription.Event.Create) // if data was added to DAB online
             {

             }
             else if (objEvent == Subscription.Event.Update) //Data was updated
             {
                 var f = Messages.FirstOrDefault(newComment);
                 if (f is not null)
                 {

                 }
             }
             else if (objEvent == Subscription.Event.Delete) //Data Removed from DB online
             {
            
             }
             Debug.WriteLine($"Event {objEvent} occurred for object: {obj.ObjectId}");
         })
         .HandleError((query, exception) =>
         {
             Debug.WriteLine($"Error in query for class {query.GetClassName()}: {exception.Message}");
         })
         .HandleUnsubscribe(query =>
         {
             Debug.WriteLine($"Unsubscribed from query: {query.GetClassName()}");
         });

         // Connect asynchronously 
//OPTIONAL, but in real world scenarios, you'd better off calling it anyway.
         await Task.Run(() => LiveClient.ConnectIfNeeded());
     }
     catch (IOException ex)
     {
         Console.WriteLine($"Connection error: {ex.Message}");
     }
     catch (Exception ex)
     {
         Debug.WriteLine($"SetupLiveQuery encountered an error: {ex.Message}");
     }
 }
```
If you need a connection listener, you can set a class as 
```
public class CLASS_NAME_FOR_LISTENERCLASS: ObservableObject, IParseLiveQueryClientCallbacks
{
//You will NEED to implement 
 public void OnLiveQueryClientConnected(ParseLiveQueryClient client)
 {
     Debug.WriteLine("Client Connected");
 }

 public void OnLiveQueryClientDisconnected(ParseLiveQueryClient client, bool userInitiated)
 {
     Debug.WriteLine("Client Disconnected");
 }

 public void OnLiveQueryError(ParseLiveQueryClient client, LiveQueryException reason)
 {
     Debug.WriteLine("Error " + reason.Message);
 }

 public void OnSocketError(ParseLiveQueryClient client, Exception reason)
 {
     Debug.WriteLine("Socket Error ");
 }
}
```
That's it!
I'll be updating the SDK with any new features if any, The .NET SDK had no Equivalent but we do now ! 
This is a simple "Port" with no real fixes implemented (except for unwanted crashes) and no security updates either. 
PRs are welcomed!
- Many thanks to [JohnMcPherson](https://github.com/JonMcPherson/parse-live-query-dotnet) for [Parse-Live-Query-Dotnet](https://github.com/JonMcPherson/parse-live-query-dotnet) . I would absolutely NOT have done it  without you!
- [Parse Community](https://github.com/parse-community)

