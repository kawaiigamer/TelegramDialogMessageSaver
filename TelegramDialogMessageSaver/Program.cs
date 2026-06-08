using TelegramDialogMessageSaver;
namespace TelegamSaver;

internal class MainApplication
{
    public static async Task Main(string[] args)
    {
        var login_data = new TelegramAccountInteractionData
        {
            phone = "",
            api_hash = "",
            api_id = 0,
            F2A_password = ""
        };

        MainAccountHandler handler = new();
        await handler.LoginTGAsync(login_data);
        await handler.StartPollingAsync(new() { });
    }
}
