using System;
using Telegram.Bot;


namespace Telegram_bot
{
    class Program
    {
        private static string token { get; set; } = "7706907828:AAG3CzS48IDwnnVhX3ur_qBw_Ys3X2KgII8";
        private static TelegramBotClient client;
        static void Main(string[] args)
        {
            client = new TelegramBotClient(token);
            client.StartReceiving();
            client.OnMessage += OnMessageHandler;
            Console.ReadLine();
            client.StopReceiving();
        }
        private static void OnMessageHandler(object sendler, MessageEvantArgs e)
        {
            var msg = e.Message;
            if (msg.Text != null)
            {
                Console.WriteLine($"Пришло сообщение с текстом: {msg.Text}");
            }
        }
    }
}
