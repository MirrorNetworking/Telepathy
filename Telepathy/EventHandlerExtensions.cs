using System;

namespace Telepathy
{
    public static class EventHandlerExtensions
    {
        public static EventArgs<T> CreateArgs<T>(this EventHandler<EventArgs<T>> _,T argument)
        {
            return new EventArgs<T>(argument);
        }

        public static EventArgs<T,U> CreateArgs<T,U>(this EventHandler<EventArgs<T,U>> _, T v1, U v2)
        {
            return new EventArgs<T,U>(v1, v2);
        }
    }

    public class EventArgs<T> : EventArgs
    {
        public EventArgs(T value)
        {
            Value = value;
        }

        public T Value { get; }
    }

    public class EventArgs<T, U> : EventArgs<T>
    {
        public EventArgs(T value, U value2): base(value)
        {
            Value2 = value2;
        }

        public U Value2 { get; }
    }
}