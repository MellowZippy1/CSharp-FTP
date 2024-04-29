// NOTE: THIS FILE MUST NOT CHANGE

namespace MessageNS
{

    public class Message
    {
        public MessageType Type { get; set; }
        public string? Content { get; set; }
    }

    public enum MessageType
    {
        Hello,
        Welcome,
        RequestData,
        Data,
        Ack,
        End,
        Error
    }

}
