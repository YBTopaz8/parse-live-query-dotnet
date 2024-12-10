namespace Parse.LiveQuery;

using System;
using System.Reactive.Linq;

public interface ISubscriptionFactory
{
    Subscription<T> CreateSubscription<T>(int requestId, ParseQuery<T> query) where T : ParseObject;
}