// incoming message queue of <connectionId, message>
// (not a HashSet because one connection can have multiple new messages)
public struct GoodOldMessage
{
    public uint connectionId;
    public GoodOldEventType eventType;
    public byte[] data;
    public GoodOldMessage(uint connectionId, GoodOldEventType eventType, byte[] data)
    {
        this.connectionId = connectionId;
        this.eventType = eventType;
        this.data = data;
    }
}