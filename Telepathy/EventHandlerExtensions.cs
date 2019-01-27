using System;

namespace Telepathy
{
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