using System.Collections.Generic;
//using Parse.Common.Internal;
//using Parse.Core.Internal;
using Parse.Infrastructure.Data;

namespace Parse.LiveQuery; 
public class SubscribeClientOperation<T> : SessionClientOperation where T : ParseObject {

    private static readonly Dictionary<string, object> EmptyJsonObject = new Dictionary<string, object>(0);

    private readonly int _requestId;
    private readonly ParseQuery<T> _query;

    internal SubscribeClientOperation(Subscription<T> subscription, string sessionToken) : base(sessionToken) {
        _requestId = subscription.RequestId;
        _query = subscription.Query;
    }

    // TODO: add support for fields
    // https://github.com/ParsePlatform/parse-server/issues/3671
    protected override IDictionary<string, object> ToJsonObject() => new Dictionary<string, object> {
        ["op"] = "subscribe",
        ["requestId"] = _requestId,
        ["query"] = new Dictionary<string, object>
        {
            ["className"] = ParseClient.Instance.CreateObjectWithoutData<T>(null).ClassName,
            //["className"] = _query.w.GetClassName(),
            ["where"] = PointerOrLocalIdEncoder.Instance.Encode(EmptyJsonObject, ParseClient.Instance.Services)
        }
    };

}
