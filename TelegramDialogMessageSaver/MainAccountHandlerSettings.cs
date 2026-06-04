namespace TelegramDialogMessageSaver
{
    internal class MainAccountHandlerSettings
    {
        public HashSet<long>? CHATS_IDS;
        public HashSet<long>? CHANNELS_IDS;
        public int MAX_DOWNLOADS = 16;
        public int MAX_SYMBOLS_FOR_DISPLAY = 128;
        public string DATETIME_FORMAT = "dd.MM.yyyy HH:mm:ss";
        public string DATABASE_PATH = "TelegramChatMessages.db";
        public Dictionary<string, int> LIMITS_MB = new()
        {
            { "/ogg", 24},
            { "webp", 4},
            { "/mp4", 64},
            { ".mp3", 32},
            { ".mp4", 96},
            { ".pdf", 96},
            { ".7z", 196},
            { ".zip", 196},
            { ".rar", 196},
            { ".png", 36},
            { ".psd", 64},
            { ".torrent", 2},
            { ".apk", 0},
            { ".exe", 0},
            { "other", 12}
        };
    }
}
