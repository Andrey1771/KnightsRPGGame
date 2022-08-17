using System;
using System.Collections.Generic;
using System.Linq;

namespace EventBusLibrary
{
    internal static class EventBusHelper
    {
        private static Dictionary<Type, List<Type>> s_CashedSubscriberTypes =
            new Dictionary<Type, List<Type>>();

        public static List<Type> GetSubscriberTypes(
            IGlobalSubscriber globalSubscriber)
        {
            var type = globalSubscriber.GetType();
            if (s_CashedSubscriberTypes.ContainsKey(type))
            {
                return s_CashedSubscriberTypes[type];
            }

            var subscriberTypes = type
                .GetInterfaces()
                .Where(t => t.GetInterfaces()
                    .Contains(typeof(IGlobalSubscriber)))
                .ToList();

            s_CashedSubscriberTypes[type] = subscriberTypes;
            return subscriberTypes;
        }
    }
}
