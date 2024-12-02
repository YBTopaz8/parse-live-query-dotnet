using System.Threading.Tasks;

namespace Parse.LiveQuery; 

public interface IWebSocketClient {

    Task Open();

    Task Close();

    Task Send(string message);

    WebSocketClientState State { get; }
}
