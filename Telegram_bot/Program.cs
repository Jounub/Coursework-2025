using System;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;

namespace Telegram_bot
{
    class Program
    {
        private static string token { get; set; } = "7706907828:AAG3CzS48IDwnnVhX3ur_qBw_Ys3X2KgII8";
        private static TelegramBotClient client;
        
        static void Main(string[] args)
        {
            client = new TelegramBotClient(token);
            client.OnMessage += OnMessageHandler;
            client.StartReceiving();
            
            Console.WriteLine("Бот запущен! Нажмите Enter для остановки...");
            Console.ReadLine();
            
            client.StopReceiving();
        }

        private static async void OnMessageHandler(object sender, MessageEventArgs e)
        {
            var msg = e.Message;
            
            if (msg.Text != null)
            {
                Console.WriteLine($"Пришло сообщение от {msg.From.FirstName}: {msg.Text}");
                
                // Обработка команд
                switch (msg.Text.Split(' ')[0].ToLower())
                {
                    case "/start":
                        await HandleStartCommand(msg);
                        break;
                        
                    case "/search":
                        await HandleSearchCommand(msg);
                        break;
                        
                    default:
                        await client.SendTextMessageAsync(
                            msg.Chat.Id,
                            "Используйте команды:\n/start - начало работы\n/search - поиск группы");
                        break;
                }
            }
        }

        private static async Task HandleStartCommand(Message msg)
        {
            string response = "Привет! Я бот для работы с расписанием.\n\n" +
                             "Доступные команды:\n" +
                             "/start - показать это сообщение\n" +
                             "/search [название] - поиск группы (например: /search РИЗ)";
            
            await client.SendTextMessageAsync(
                msg.Chat.Id,
                response);
        }

        private static async Task HandleSearchCommand(Message msg)
        {
            string searchQuery = msg.Text.Length > "/search".Length 
                ? msg.Text.Substring("/search".Length).Trim() 
                : string.Empty;

            if (string.IsNullOrEmpty(searchQuery))
            {
                await client.SendTextMessageAsync(
                    msg.Chat.Id,
                    "Пожалуйста, укажите название группы для поиска.\nПример: /search РИЗ");
                return;
            }

            // Здесь будет логика поиска групп
            // Пока просто имитируем ответ
            string response = $"🔍 Результаты поиска по запросу \"{searchQuery}\":\n\n" +
                             "1. РИЗ-200543\n" +
                             "2. РИЗ-200542\n" +
                             "3. РИЗБ-200541\n\n" +
                             "Выберите нужную группу, отправив её номер.";
            
            await client.SendTextMessageAsync(
                msg.Chat.Id,
                response);
        }
    }
}
