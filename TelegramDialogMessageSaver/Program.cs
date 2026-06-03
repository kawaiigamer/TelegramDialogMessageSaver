using TelegramDialogMessageSaver;
namespace TelegamSaver;

internal class MainApplication
{
    public static async Task Main(string[] args)
    {
        var login_data = new TelegramAccountInterractionData
        {
            phone = "",
            api_hash = "",
            api_id = ,
            F2A_password = ""
        };

        MainAccountHandler handler = new MainAccountHandler(login_data);
        await handler.LoginTGAsync();
        await handler.StartPollingAsync();
    }
}
