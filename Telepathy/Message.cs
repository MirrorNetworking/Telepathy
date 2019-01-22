// incoming message queue of <connectionId, message>
// (not a HashSet because one connection can have multiple new messages)
// -> a class, so that we don't copy the whole struct each time
namespace Telepathy
{
    public class Message
    {
        public int connectionId;
        public EventType eventType;
        public byte[] data;
        public Message(int connectionId, EventType eventType, byte[] data)
        {
            this.connectionId = connectionId;
            this.eventType = eventType;
            this.data = data;
        }
    }
}