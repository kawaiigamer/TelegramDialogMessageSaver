using TelegamSaver;
using TL;
using WTelegram;

namespace TelegramDialogMessageSaver
{
    internal class MainAccountHandler
    {
        private readonly SemaphoreSlim Semaphore = new SemaphoreSlim(MAX_DOWNLOADS);
        private readonly TelegramAccountInterractionData LoginData;
        
        private UpdateManager Manager;
        private Client Client;      

        private const int MAX_DOWNLOADS = 16;
        private const int MAX_SYMBOLS_FOR_DISPLAY = 128;
        private const string DATETIME_FORMAT = "dd.MM.yyyy HH:mm:ss";
        private static Dictionary<string, int> LIMITS_MB = new()
        {
            { "/ogg", 24},
            { "webp", 4},
            { "/mp4", 64},
            { ".mp3", 32},
            { ".mp4", 96},
            { ".pdf", 96},
            //{ ".7z", 196},
            { ".zip", 196},
            { ".rar", 196},
            { ".png", 36},
            { ".apk", 0},
            { ".exe", 0},
            { "other", 12}
        };

        private TL.User? GetUserByID(long id) => Manager.Users.TryGetValue(id, out var user) ? user : null;
        private static bool IsIndividualChat(Peer peer) => peer is PeerUser;
        private static string ReduceStr(string str) => str.Length <= MAX_SYMBOLS_FOR_DISPLAY ? str : $"{str.Substring(0, MAX_SYMBOLS_FOR_DISPLAY)}.....";
        private static void Log(string msg) => Console.WriteLine($"[{DateTime.Now.ToString(DATETIME_FORMAT)}] {ReduceStr(msg)}");

        private static bool IsOverMaxSize(string media_type, long doc_size) => doc_size > (LIMITS_MB.TryGetValue(media_type[^4..], out int max_size) ? max_size : LIMITS_MB["other"]) * 1024 * 1024;

        public MainAccountHandler(TelegramAccountInterractionData data)
        {
            LoginData = data;
        }

        public async Task LoginTGAsync()
        {
            Client = new WTelegram.Client(LoginData.api_id, LoginData.api_hash);
            await DoLogin(LoginData.phone);
            Manager = Client.WithUpdateManager(OnUpdateHandler);

            async Task DoLogin(string loginInfo)
            {
                while (Client.User == null)
                {
                    switch (await Client.Login(loginInfo))
                    {
                        case "verification_code": Console.Write("Code: "); loginInfo = Console.ReadLine(); break;
                        case "name": loginInfo = LoginData.api_hash; break;
                        case "password": loginInfo = LoginData.F2A_password; break;
                        default: loginInfo = null; break;
                    }
                    Log($"We are logged-in as {Client.User}, ID: {Client.User?.id}");
                }
            }
        }

        public async Task StartPollingAsync(CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private bool GetMessageInfo(MessageBase mb, out Message? message, out long from_user_id)
        {
            message = mb as Message;
            if (message == null)
            {
                from_user_id = 0;
                return false;
            }
            from_user_id = message.peer_id.ID;
            return from_user_id != 0 && IsIndividualChat(message.peer_id);
        }

        private InternalUser BuildUser(long id)
        {
            InternalUser sender = new() { user_id = id, messages = [] };

            TL.User? sender_profile = GetUserByID(id);
            if (sender_profile != null)
            {
                sender.username = sender_profile.username;
                sender.name = $"{sender_profile.first_name} {sender_profile.last_name}";
                sender.info = sender_profile.MainUsername;
            }
            return sender;
        }

        private async Task<byte[]> DownloadDocument(Func<MemoryStream, Task> f)
        {
            await Semaphore.WaitAsync();
            try
            {
                using var memory_stream = new MemoryStream();
                await f(memory_stream);
                return memory_stream.ToArray();
            }
            finally
            {
                Console.WriteLine($"Releasing download documents semaphore, {Semaphore.Release()}\\{MAX_DOWNLOADS} slots empty");
            }
        }       

        private async Task<InternalUserMessage> BuildUserMessage(ApplicationStorageContext db, Message message)
        {            
            InternalUserMessage sender_message = new()
            {
                direction = message.flags.HasFlag(Message.Flags.out_) ? InternalUserMessageDirection.OUTGOING : InternalUserMessageDirection.INCOMING,
                text = message.message,
                date = DateTime.Now,
                reply_msg_id = message.ReplyHeader != null ? message.ReplyHeader.reply_to_msg_id : 0,
                forward_from = message.fwd_from != null ? message.fwd_from.from_id?.ID : 0,
                dialog_id = message.id
            };

            switch (message.media)
            {
                case TL.MessageMediaDocument:
                    if (message.media is TL.MessageMediaDocument { document: Document doc })
                    {
                        sender_message.media_hash = doc.access_hash;
                        if (!db.IsMediaHashExists(sender_message.media_hash))
                        {
                            string media_type = doc.Filename ?? doc.mime_type;
                            if (IsOverMaxSize(media_type, doc.size))
                            {
                                Log($"Recived MessageMediaDocument is too big for it type {media_type} - {doc.size} bytes");
                                break;
                            }
                            InternalUserMessageMedia media = new() { hash = sender_message.media_hash, media_type = media_type, media = await DownloadDocument(async (memory_stream) => await Client.DownloadFileAsync(doc, memory_stream)) };
                            db.Media.Add(media);
                            Log($"Reciving MessageMediaDocument: {media.media_type} - {sender_message.media_hash}");
                        } else
                        {
                            Log($"Recived already saved MessageMediaDocument: {sender_message.media_hash}");
                        }
                    }
                    
                    break;

                case TL.MessageMediaPhoto:
                    if (message.media is TL.MessageMediaPhoto { photo: Photo photo })
                    {
                        sender_message.media_hash = photo.access_hash;
                        if (!db.IsMediaHashExists(sender_message.media_hash))
                        {
                            InternalUserMessageMedia media = new(){ hash = sender_message.media_hash, media_type = "photo", media = await DownloadDocument(async (memory_stream) => await Client.DownloadFileAsync(photo, memory_stream)) };
                            db.Media.Add(media);
                            Log($"Reciving new photo: {sender_message.media_hash}");
                        } else
                        {
                            Log($"Recived already saved photo: {sender_message.media_hash}");
                        }
                    }
                    break;

                case null:
                    break;

                default:
                    Log($"Unknown media: {message.media?.GetType()}");
                    break;
            }
            return sender_message;
        }

        private InternalUser GetOrAddUser(ApplicationStorageContext db, long user_id)
        {
            InternalUser? sender = db.Users.Where(x => x.user_id == user_id).FirstOrDefault();
            if (sender == null)
            {
                sender = BuildUser(user_id);
                Log($"Adding new user to database: {user_id}, {sender.username}, {sender.name}, {sender.info}");
                db.Users.Add(sender);
            }
            if (sender.messages == null)
            {
                sender.messages = [];
            }
            return sender;
        }

        private async Task OnUpdateHandler(Update update)
        {
            switch (update)
            {
                case UpdateNewMessage msg:
                    if (!GetMessageInfo(msg.message, out Message? message, out long from_user_id))
                    {
                        break;
                    }
                    Log($"New message in chat ID: {from_user_id}, Text: {message!.message}");

                    using (ApplicationStorageContext db = new ApplicationStorageContext())
                    {
                        InternalUser sender = GetOrAddUser(db, from_user_id);

                        InternalUserMessage sender_message = await BuildUserMessage(db, message);

                        sender.messages.Add(sender_message);
                        db.SaveChanges();
                    }
                    break;

                case UpdateEditMessage msg:
                    if (!GetMessageInfo(msg.message, out Message? edited_message, out long edited_message_from_user_id))
                    {
                        break;
                    }
                    Log($"Message edited in chat ID: {edited_message_from_user_id}, Text: {edited_message!.message}");

                    using (ApplicationStorageContext db = new ApplicationStorageContext())
                    {
                        InternalUser sender = GetOrAddUser(db, edited_message_from_user_id);

                        InternalUserMessage? target_message = sender.messages.Where(x => x.dialog_id == edited_message_from_user_id).FirstOrDefault();
                        if (target_message == null)
                        {
                            InternalUserMessage first_message = await BuildUserMessage(db, edited_message);
                            sender.messages.Add(first_message);
                        }
                        else
                        {
                            InternalUserMessage edited_message_final = target_message with { text = msg.message.ToString() ?? "NULL" };
                            sender.messages.Add(edited_message_final);
                        }
                        Log($"Edited message in chat ID: {sender.Id}, User: {sender.name} {sender.username}, Text: {edited_message.message}");
                        db.SaveChanges();
                    }
                    break;

                case UpdateReadHistoryOutbox msg:
                    Log($"The interlocutor in chat ID: {msg.peer.ID} has read messages up to message ID: {msg.max_id}");
                    break;

                case UpdateUserStatus msg:
                    Log($"Received UpdateUserStatus in chat ID: {msg.user_id}");
                    break;

                default:
                    Log($"Received unhandled update type: {update.GetType().Name}");
                    break;
            }
        }
    }
}
