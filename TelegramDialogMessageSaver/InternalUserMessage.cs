namespace TelegamSaver
{
    internal record InternalUserMessage
    {
        public int Id { get; set; }

        public int dialog_id { get; set; }
        public InternalUserMessageDirection direction { get; set; }
        public long? forward_from { get; set; }
        public int? reply_msg_id { get; set; }
        public string? text { get; set; }

        public DateTime date { get; set; }

        public long media_hash { get; set; }

        public List<InternalUserMessage> versions;
    }
}
