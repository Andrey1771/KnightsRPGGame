using System;
using System.Collections.Generic;

namespace EventBusLibrary
{
    public static class EventBus
    {
        private static Dictionary<Type, SubscribersList<IGlobalSubscriber>> s_Subscribers
            = new Dictionary<Type, SubscribersList<IGlobalSubscriber>>();

        public static void Subscribe(IGlobalSubscriber subscriber)
        {
            var subscriberTypes = EventBusHelper.GetSubscriberTypes(subscriber);
            foreach (var t in subscriberTypes)
            {
                if (!s_Subscribers.ContainsKey(t))
                {
                    s_Subscribers[t] = new SubscribersList<IGlobalSubscriber>();
                }
                s_Subscribers[t].Add(subscriber);
            }
        }

        public static void Unsubscribe(IGlobalSubscriber subscriber)
        {
            var subscriberTypes = EventBusHelper.GetSubscriberTypes(subscriber);
            foreach (var t in subscriberTypes)
            {
                if (s_Subscribers.ContainsKey(t))
                    s_Subscribers[t].Remove(subscriber);
            }
        }

        public static void RaiseEvent<TSubscriber>(Action<TSubscriber> action)
            where TSubscriber : class, IGlobalSubscriber
        {
            var subscribers = s_Subscribers[typeof(TSubscriber)];

            subscribers.Executing = true;
            foreach (var subscriber in subscribers.List)
            {
                try
                {
                    action.Invoke(subscriber as TSubscriber);
                }
                catch (Exception e)
                {
                    //Debug.LogError(e);
                }
            }
            subscribers.Executing = false;
            subscribers.Cleanup();
        }
    }
}
