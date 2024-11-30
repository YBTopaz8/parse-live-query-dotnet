﻿using System.Collections.Generic;
using System.Text.Json;

namespace Parse.LiveQuery; 
public class UnsubscribeClientOperation : IClientOperation {

    private readonly int _requestId;

    internal UnsubscribeClientOperation(int requestId) {
        _requestId = requestId;
    }

    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["op"] = "unsubscribe",
        ["requestId"] = _requestId
    });
}
