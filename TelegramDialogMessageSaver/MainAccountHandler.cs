using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using TelegamSaver;
using TL;
using WTelegram;

namespace TelegramDialogMessageSaver
{
    internal class MainAccountHandler
    {
        private SemaphoreSlim semaphore;
        private MainAccountHandlerSettings settings;
        private UpdateManager manager;
        private Client client;
        private ApplicationStorageContext db;
        private TL.User? GetUserByID(long id) => manager.Users.TryGetValue(id, out var user) ? user : null;
        private TL.ChatBase? GetChannelByID(long id) => manager.Chats.TryGetValue(id, out var channel) ? channel : null;
        private bool IsIndividualChat(Peer peer) => peer is PeerUser;
        private bool IsChannel(Peer peer) => peer is PeerChat || peer is PeerChannel;
        private string ReduceStr(string str) => str.Length <= settings.MAX_SYMBOLS_FOR_DISPLAY ? str : $"{str.Substring(0, settings.MAX_SYMBOLS_FOR_DISPLAY)}.....";
        private void Log(string msg) => Console.WriteLine($"[{DateTime.Now.ToString(settings.DATETIME_FORMAT)}] {ReduceStr(msg)}");
        private bool IsOverMaxSize(string media_type, long doc_size) => doc_size > (settings.LIMITS_MB.TryGetValue(GetExnensionOrDefault(media_type), out int max_size) ? max_size : settings.LIMITS_MB["other"]) * 1024 * 1024;

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
            client = new WTelegram.Client(login_data.api_id, login_data.api_hash);
            await DoLogin(login_data.phone);
            manager = client.WithUpdateManager(OnUpdateHandler);

            async Task DoLogin(string login_info)
            {
                while (client.User == null)
                {
                    switch (await client.Login(login_info))
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
            this.settings = settings;
            semaphore = new SemaphoreSlim(this.settings.MAX_DOWNLOADS);
            db = new(settings.DATABASE_PATH);
            try
            {
                Log($"We are logged-in as {client.User}, ID: {client.User?.id}");
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                await db.DisposeAsync();
                await client.DisposeAsync();
            }
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
                return settings.CHATS_IDS == null || settings.CHATS_IDS.Contains(from_user_id);
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
                return settings.CHANNELS_IDS == null || settings.CHANNELS_IDS.Contains(from_channel_id);
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
            await semaphore.WaitAsync();
            try
            {
                using var memory_stream = new MemoryStream();
                await f(memory_stream);
                return memory_stream.ToArray();
            }
            finally
            {
                Console.WriteLine($"Releasing download documents semaphore, {semaphore.Release()}\\{settings.MAX_DOWNLOADS} slots empty");
            }
        }       

        private async Task<InternalUserMessage> BuildUserMessage(Message message)
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
                        if (!await db.IsMediaHashExists(doc.access_hash))
                        {
                            string media_type = doc.Filename ?? doc.mime_type;
                            if (IsOverMaxSize(media_type, doc.size))
                            {
                                new_message.media_hash = -1;
                                Log($"Recived MessageMediaDocument is too big for it type {media_type} - {doc.size} bytes");
                                break;
                            }                            
                            InternalUserMessageMedia media = new() { hash = doc.access_hash, media_type = media_type, media = await DownloadDocument(async (memory_stream) => await client.DownloadFileAsync(doc, memory_stream)) };
                            await db.Media.AddAsync(media);
                            Log($"Reciving MessageMediaDocument: {media.media_type} - {media.hash}");
                        } else
                        {
                            Log($"Recived already saved MessageMediaDocument: {doc.access_hash}");
                        }
						new_message.media_hash = doc.access_hash;
                    }                    
                    break;

                case TL.MessageMediaPhoto:
                    if (message.media is TL.MessageMediaPhoto { photo: Photo photo })
                    {
                        new_message.media_hash = photo.access_hash;
                        if (!await db.IsMediaHashExists(new_message.media_hash))
                        {
                            InternalUserMessageMedia media = new(){ hash = new_message.media_hash, media_type = "photo", media = await DownloadDocument(async (memory_stream) => await client.DownloadFileAsync(photo, memory_stream)) };
                            await db.Media.AddAsync(media);
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

        private async Task<InternalUser> GetOrAddUser(long user_id)
        {
            InternalUser? sender = await db.Users.Where(x => x.user_id == user_id).FirstOrDefaultAsync();
            if (sender == null)
            {
                sender = BuildUser(user_id);
                Log($"Adding new user to database: {user_id}, {sender.username}, {sender.name}, {sender.info}");
                await db.Users.AddAsync(sender);
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
            InternalUser sender = await GetOrAddUser(from_chat_id);
            InternalUserMessage new_message = await BuildUserMessage(message);
            new_message.from_user = sender;
            await db.Messages.AddAsync(new_message);
            await db.SaveChangesAsync();            
        }

        private async Task SaveNewChannelMessage(MessageBase mb)
        {
            if (!GetChannelMessageInfo(mb, out Message? channel_message, out long from_channel_user_id, out long from_channel_id))
            {
                return;
            }
            Log($"New message in channel ID: {from_channel_id}, from user ID: {from_channel_user_id}, Text: {channel_message!.message}");
            InternalChannel? channel = await db.Channels.Where(x => x.channel_id == from_channel_id).FirstOrDefaultAsync();
            if (channel == null)
            {
                var channel_data = GetChannelByID(from_channel_id);
                if(channel_data != null)
                {
                    channel = new InternalChannel() { channel_id = channel_data.ID, title = channel_data.Title, username = channel_data.MainUsername, type = channel_data.IsChannel ? InternalChannelType.CHANNEL : InternalChannelType.GROUP };
                    Log($"Adding new {channel.type} to database: {channel.channel_id}, {channel.title}, {channel.username}");
                    await db.Channels.AddAsync(channel);
                }
            }
            InternalUser sender = await GetOrAddUser(from_channel_user_id);
            InternalUserMessage new_message = await BuildUserMessage(channel_message);
            new_message.from_user = sender;
            new_message.from_channel = channel;
            await db.Messages.AddAsync(new_message);
            await db.SaveChangesAsync();
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
