namespace TelegamSaver
{
    internal class InternalUser
    {
        public int Id { get; set; }
        public long user_id { get; set; }
        public string name { get; set; }

        public string? info { get; set; }
        public string? username { get; set; }

        public List<InternalUserMessage> messages { get; set; }
    }
}
