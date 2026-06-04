using TelegamSaver;
using TL;
using WTelegram;

namespace TelegramDialogMessageSaver
{
    internal class MainAccountHandler
    {
        private SemaphoreSlim Semaphore;
        private MainAccountHandlerSettings Settings;
        private UpdateManager Manager;
        private Client Client; 
        private TL.User? GetUserByID(long id) => Manager.Users.TryGetValue(id, out var user) ? user : null;
        private TL.ChatBase? GetChannelByID(long id) => Manager.Chats.TryGetValue(id, out var channel) ? channel : null;
        private bool IsIndividualChat(Peer peer) => peer is PeerUser;
        private bool IsChannel(Peer peer) => peer is PeerChat || peer is PeerChannel;
        private string ReduceStr(string str) => str.Length <= Settings.MAX_SYMBOLS_FOR_DISPLAY ? str : $"{str.Substring(0, Settings.MAX_SYMBOLS_FOR_DISPLAY)}.....";
        private void Log(string msg) => Console.WriteLine($"[{DateTime.Now.ToString(Settings.DATETIME_FORMAT)}] {ReduceStr(msg)}");
        private bool IsOverMaxSize(string media_type, long doc_size) => doc_size > (Settings.LIMITS_MB.TryGetValue(GetExnensionOrDefault(media_type), out int max_size) ? max_size : Settings.LIMITS_MB["other"]) * 1024 * 1024;

        private static string GetExnensionOrDefault(string media_type)
        {
            int index_point = media_type.LastIndexOf(".");
            if (index_point == -1)
            {
                return media_type;
            }
            return media_type.Substring(index_point);
        }        

        public async Task LoginTGAsync(TelegramAccountInterractionData login_data)
        {
            Client = new WTelegram.Client(login_data.api_id, login_data.api_hash);
            await DoLogin(login_data.phone);
            Manager = Client.WithUpdateManager(OnUpdateHandler);

            async Task DoLogin(string login_info)
            {
                while (Client.User == null)
                {
                    switch (await Client.Login(login_info))
                    {
                        case "verification_code":
                            Console.Write("Code: ");
                            login_info = Console.ReadLine();
                            break;
                        case "name":
                            login_info = login_data.api_hash;
                            break;
                        case "password":
                            login_info = login_data.F2A_password;
                            break;
                        default:
                            login_info = null;
                            break;
                    }                    
                }
            }
        }

        public async Task StartPollingAsync(MainAccountHandlerSettings settings, CancellationToken ct = default)
        {
            Settings = settings;
            Semaphore = new SemaphoreSlim(Settings.MAX_DOWNLOADS);
            try
            {
                Log($"We are logged-in as {Client.User}, ID: {Client.User?.id}");
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException) {}
        }

        private bool GetIndividualChatMessageInfo(MessageBase mb, out Message? message, out long from_user_id)
        {
            message = mb as Message;
            if (message == null)
            {
                from_user_id = 0;
                return false;
            }
            from_user_id = message.peer_id.ID;
            if (IsIndividualChat(message.peer_id))
            {
                return Settings.CHATS_IDS == null || Settings.CHATS_IDS.Contains(from_user_id);
            } 
            return false;     
        }

        private bool GetChannelMessageInfo(MessageBase mb, out Message? message, out long from_user_id, out long from_channel_id)
        {
            message = mb as Message;
            if (message == null)
            {
                from_user_id = from_channel_id = 0;
                return false;
            }
            from_user_id = message.from_id.ID;
            from_channel_id = message.peer_id.ID;
            if (IsChannel(message.peer_id))
            {
                return Settings.CHANNELS_IDS == null || Settings.CHANNELS_IDS.Contains(from_channel_id);
            }
            return false;
        }
        private InternalUser BuildUser(long id)
        {
            InternalUser sender = new() { user_id = id };

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
                Console.WriteLine($"Releasing download documents semaphore, {Semaphore.Release()}\\{Settings.MAX_DOWNLOADS} slots empty");
            }
        }       

        private async Task<InternalUserMessage> BuildUserMessage(ApplicationStorageContext db, Message message)
        {            
            InternalUserMessage new_message = new()
            {
                direction = message.flags.HasFlag(Message.Flags.out_) ? InternalUserMessageDirection.OUTGOING : InternalUserMessageDirection.INCOMING,
                text = message.message,
                date = DateTime.Now,
                reply_msg_id = message.ReplyHeader != null ? message.ReplyHeader.reply_to_msg_id : 0,
                forwarded_from = message.fwd_from != null ? message.fwd_from.from_id?.ID : 0,
                dialog_id = message.id
            };
            switch (message.media)
            {
                case TL.MessageMediaDocument:
                    if (message.media is TL.MessageMediaDocument { document: Document doc })
                    {
                        new_message.media_hash = doc.access_hash;
                        if (!db.IsMediaHashExists(new_message.media_hash))
                        {
                            string media_type = doc.Filename ?? doc.mime_type;
                            if (IsOverMaxSize(media_type, doc.size))
                            {
                                Log($"Recived MessageMediaDocument is too big for it type {media_type} - {doc.size} bytes");
                                break;
                            }
                            InternalUserMessageMedia media = new() { hash = new_message.media_hash, media_type = media_type, media = await DownloadDocument(async (memory_stream) => await Client.DownloadFileAsync(doc, memory_stream)) };
                            db.Media.Add(media);
                            Log($"Reciving MessageMediaDocument: {media.media_type} - {new_message.media_hash}");
                        } else
                        {
                            Log($"Recived already saved MessageMediaDocument: {new_message.media_hash}");
                        }
                    }                    
                    break;

                case TL.MessageMediaPhoto:
                    if (message.media is TL.MessageMediaPhoto { photo: Photo photo })
                    {
                        new_message.media_hash = photo.access_hash;
                        if (!db.IsMediaHashExists(new_message.media_hash))
                        {
                            InternalUserMessageMedia media = new(){ hash = new_message.media_hash, media_type = "photo", media = await DownloadDocument(async (memory_stream) => await Client.DownloadFileAsync(photo, memory_stream)) };
                            db.Media.Add(media);
                            Log($"Reciving new photo: {new_message.media_hash}");
                        } else
                        {
                            Log($"Recived already saved photo: {new_message.media_hash}");
                        }
                    }
                    break;

                case null:
                    break;

                default:
                    Log($"Unknown media: {message.media?.GetType()}");
                    break;
            }
            return new_message;
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
            return sender;
        }

        private async Task SaveNewMessage(MessageBase mb)
        {
            if (!GetIndividualChatMessageInfo(mb, out Message? message, out long from_chat_id))
            {
                return;
            }
            Log($"New message in chat ID: {from_chat_id}, Text: {message!.message}");
            using (ApplicationStorageContext db = new(Settings.DATABASE_PATH))
            {
                InternalUser sender = GetOrAddUser(db, from_chat_id);
                InternalUserMessage new_message = await BuildUserMessage(db, message);
                new_message.from_user = sender;
                db.Messages.Add(new_message);
                db.SaveChanges();
            }
        }

        private async Task SaveNewChannelMessage(MessageBase mb)
        {
            if (!GetChannelMessageInfo(mb, out Message? channel_message, out long from_channel_user_id, out long from_channel_id))
            {
                return;
            }
            Log($"New message in channel ID: {from_channel_id}, from user ID: {from_channel_user_id}, Text: {channel_message!.message}");
            using (ApplicationStorageContext db = new(Settings.DATABASE_PATH))
            {
                InternalChannel? channel = db.Channels.Where(x => x.chnnel_id == from_channel_id).FirstOrDefault();
                if (channel == null)
                {
                    var channel_data = GetChannelByID(from_channel_id);
                    channel = new InternalChannel() { chnnel_id = channel_data.ID, title = channel_data.Title, username = channel_data.MainUsername, type = channel_data.IsChannel ? InternalChannelType.CHANNEL : InternalChannelType.GROUP };
                    Log($"Adding new {channel.type} to database: {channel.chnnel_id}, {channel.title}, {channel.username}");
                    db.Channels.Add(channel);
                }
                InternalUser sender = GetOrAddUser(db, from_channel_user_id);
                InternalUserMessage new_message = await BuildUserMessage(db, channel_message);
                new_message.from_user = sender;
                new_message.from_channel = channel;
                db.Messages.Add(new_message);
                db.SaveChanges();
            }
        }

        private async Task OnUpdateHandler(Update update)
        {
            switch (update)
            {
                case UpdateNewChannelMessage msg:
                    await SaveNewChannelMessage(msg.message);
                    break;

                case UpdateEditChannelMessage msg:
                    await SaveNewChannelMessage(msg.message);
                    break;

                case UpdateNewMessage msg:
                    await SaveNewMessage(msg.message);
                    break;

                case UpdateEditMessage msg:
                    await SaveNewMessage(msg.message);
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
