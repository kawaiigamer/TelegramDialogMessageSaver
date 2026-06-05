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
            { "audio/ogg", 24},
            { "video/mp4", 64},
            { ".webp", 4},
            { ".mp3", 32},
            { ".mp4", 96},
            { ".MP4", 6}, // "GIF"
            { ".pdf", 96},
            { ".avi", 32},
            { ".7z", 196},
            { ".zip", 196},
            { ".rar", 196},
            { ".png", 36},
            { ".jpeg", 8},
            { ".gif", 8},
            { ".jpg", 8},
            { ".amr", 18}, 
            { ".psd", 64},
            { ".html", 8},
            { ".mhtml", 64},
            { ".torrent", 2},
            { ".apk", 36},
            { ".exe", 36},
            { "other", 12}
        };
    }
}
