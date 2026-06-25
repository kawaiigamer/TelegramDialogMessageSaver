## TelegramDialogMessageSaver
Saves messages from tg as they are received. Counters those who like to change/delete messages. Does not mark the chat as read. Saves any media, even with an auto-delete timer, without opening them. Supports a limit on simultaneous downloads so as not to clog the entire channel if it is limited, session saving and login with F2A.

Also [supports](https://github.com/kawaiigamer/TelegramDialogMessageSaver/blob/master/TelegramDialogMessageSaver/MainAccountHandlerSettings.cs#L6) specify group or channel IDs, or you can disable message processing for all of them.
```c#
CHANNELS_IDS = [0]
```
The [same](https://github.com/kawaiigamer/TelegramDialogMessageSaver/blob/master/TelegramDialogMessageSaver/MainAccountHandlerSettings.cs#L5) applies to user chats.
```c#
CHATS_IDS = [100500, 500100]
```
Or leave this value `null` to enable support for anything.

Each type of document when it larger than (MB) may has separate [restrictions](https://github.com/kawaiigamer/TelegramDialogMessageSaver/blob/master/TelegramDialogMessageSaver/MainAccountHandlerSettings.cs#L12):
```c#
{ ".png", 12}
```
To exclude the storage(ex: for all unlisted documents):
```c#
{ "other", 0}
```
