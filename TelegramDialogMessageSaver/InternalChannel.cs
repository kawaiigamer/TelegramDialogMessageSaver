namespace TelegramDialogMessageSaver
{
    internal record InternalChannel
    {
        public long chnnel_id { get; set; }
        public string title { get; set; }
        public string? username { get; set; }
        public InternalChannelType type { get; set; }
    }
}
