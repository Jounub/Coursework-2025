using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace test.Services;

public class TelegramBotService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramBotService> _logger;
    private readonly IConfiguration _config;
    private readonly ScheduleParser _scheduleParser;

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        IConfiguration config,
        ScheduleParser scheduleParser)
    {
        _logger = logger;
        _config = config;
        _botClient = new TelegramBotClient(_config["TelegramBot:Token"]!);
        _scheduleParser = scheduleParser;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting bot...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
            ThrowPendingUpdates = true
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken
        );

        var me = await _botClient.GetMeAsync(cancellationToken);
        _logger.LogInformation($"Bot @{me.Username} started!");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.Message:
                    await HandleMessageAsync(update.Message!, cancellationToken);
                    break;

                case UpdateType.CallbackQuery:
                    await HandleCallbackQueryAsync(update.CallbackQuery!, cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update");
        }
    }

    private async Task HandleMessageAsync(
        Message message,
        CancellationToken cancellationToken)
    {
        if (message.Text is not { } messageText)
            return;

        var chatId = message.Chat.Id;

        // Обработка команды /start
        if (messageText.StartsWith("/start"))
        {
            await SendWelcomeMessage(chatId, cancellationToken);
            return;
        }

        // Обработка поиска групп
        if (messageText.StartsWith("/search "))
        {
            var searchQuery = messageText[8..].Trim();
            await HandleGroupSearch(chatId, searchQuery, cancellationToken);
            return;
        }

        // Обработка прямого ввода номера группы
        if (IsPotentialGroupName(messageText))
        {
            await HandleGroupSearch(chatId, messageText, cancellationToken);
            return;
        }

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Используйте /search для поиска группы или введите номер группы",
            cancellationToken: cancellationToken);
    }

    private async Task SendWelcomeMessage(long chatId, CancellationToken ct)
    {
        var welcomeText = @"📚 <b>Бот расписания УрФУ</b>

Для поиска расписания используйте:
/search [номер группы]  
Пример: /search РИЗ-220501

Или просто введите номер группы";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: welcomeText,
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    private async Task HandleGroupSearch(
        long chatId,
        string searchQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.SendChatActionAsync(
                chatId: chatId,
                chatAction: ChatAction.Typing,
                cancellationToken: cancellationToken);

            var keyboard = await _scheduleParser.SearchGroupsAsync(searchQuery);

            if (keyboard == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Группы не найдены. Проверьте правильность запроса.",
                    cancellationToken: cancellationToken);
                return;
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🔍 Найденные группы:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search error");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при поиске. Попробуйте позже.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Data!.StartsWith("group_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var groupId = parts[1];
            var groupTitle = parts[2];
            await HandleGroupSelection(
                callbackQuery.Message!.Chat.Id,
                groupId,
                groupTitle,
                callbackQuery.Id,
                cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("month_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var groupId = parts[1];
            var month = int.Parse(parts[2]);
            var groupTitle = parts[3];

            await SendDaySelection(
                callbackQuery.Message!.Chat.Id,
                groupId,
                groupTitle,
                month,
                cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("day_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var groupId = parts[1];
            var month = int.Parse(parts[2]);
            var day = int.Parse(parts[3]);
            var oneDay = int.Parse(parts[4]);
            var groupTitle = parts[5];

            var selectedDate = new DateTime(DateTime.Now.Year, month, day);
            bool oneDaySchedule = Convert.ToBoolean(oneDay);
            await GetWeekSchedule(
                callbackQuery.Message!.Chat.Id,
                groupId,
                groupTitle,
                selectedDate,
                cancellationToken,
                oneDaySchedule);
        }

        await _botClient.AnswerCallbackQueryAsync(
            callbackQueryId: callbackQuery.Id,
            cancellationToken: cancellationToken);
    }

    private async Task GetWeekSchedule(
        long chatId,
        string groupId,
        string groupTitle,
        DateTime selectedDate,
        CancellationToken cancellationToken,
        bool oneDaySchedule)
    {
        try
        {
            await _botClient.SendChatActionAsync(
                chatId: chatId,
                chatAction: ChatAction.Typing,
                cancellationToken: cancellationToken);

            var startOfWeek = selectedDate;
            DateTime endOfWeek;
            if (oneDaySchedule)
                endOfWeek = startOfWeek;
            else endOfWeek = startOfWeek.AddDays(6);

            var schedule = await _scheduleParser.GetGroupScheduleAsync(groupId, groupTitle, startOfWeek, endOfWeek);

            if (string.IsNullOrEmpty(schedule))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Не удалось загрузить расписание для выбранной группы и недели",
                    cancellationToken: cancellationToken);
                return;
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: schedule,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Schedule load error for group {groupId}");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при загрузке расписания",
                cancellationToken: cancellationToken);
        }
    }

    private bool IsPotentialGroupName(string input)
    {
        return input.Length >= 5 &&
               input.Any(char.IsLetter) &&
               input.Any(char.IsDigit);
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException =>
                $"Telegram API Error: {apiRequestException.ErrorCode} - {apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogError(errorMessage);
        return Task.CompletedTask;
    }

    private async Task HandleGroupSelection(
        long chatId,
        string groupId,
        string groupTitle,
        string callbackQueryId,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendMonthSelection(chatId, groupId, groupTitle, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in group selection for group {groupTitle}");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при выборе группы",
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendMonthSelection(long chatId, string groupId, string groupTitle, CancellationToken ct)
    {
        var currentDate = DateTime.Now;
        var buttons = new List<InlineKeyboardButton[]>();

        for (int i = 1; i <= 12; i++)
        {
            var monthName = new DateTime(currentDate.Year, i, 1).ToString("MMMM");
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    monthName,
                    $"month_{groupId}_{i}_{groupTitle}")
            });
        }

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Выберите месяц для группы {groupTitle}:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendDaySelection(long chatId, string groupId, string groupTitle, int month, CancellationToken ct)
    {
        var currentDate = DateTime.Now;
        var daysInMonth = DateTime.DaysInMonth(currentDate.Year, month);

        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();

        for (int day = 1; day <= daysInMonth; day++)
        {
            row.Add(InlineKeyboardButton.WithCallbackData(
                day.ToString(),
                $"day_{groupId}_{month}_{day}_0_{groupTitle}"));

            if (row.Count == 7 || day == daysInMonth)
            {
                buttons.Add(row.ToArray());
                row.Clear();
            }
        }
        row.Add(InlineKeyboardButton.WithCallbackData("Сегодня", $"day_{groupId}_{DateTime.Today.Month}_{DateTime.Today.Day}_1_{groupTitle}"));
        row.Add(InlineKeyboardButton.WithCallbackData("Завтра", $"day_{groupId}_{DateTime.Today.Month}_{DateTime.Today.AddDays(1).Day}_1_{groupTitle}"));
        buttons.Add(row.ToArray());
        row.Clear();

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Выберите день в {new DateTime(currentDate.Year, month, 1).ToString("MMMM")}:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }
}