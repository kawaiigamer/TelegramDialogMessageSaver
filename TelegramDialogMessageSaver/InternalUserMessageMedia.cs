namespace TelegramDialogMessageSaver
{
    internal record InternalUserMessageMedia
    {
        public long hash { get; set; }

        public string? media_type { get; set; }

        public byte[]? media { get; set; }
    }
}
