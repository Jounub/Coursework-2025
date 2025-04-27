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

            // Получаем клавиатуру с группами
            var keyboard = await _scheduleParser.SearchGroupsAsync(searchQuery);

            if (keyboard == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Группы не найдены. Проверьте правильность запроса.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Отправляем сообщение с кнопками выбора группы
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
            var groupId = callbackQuery.Data[6..];
            await HandleGroupSelection(
                callbackQuery.Message!.Chat.Id,
                groupId,
                callbackQuery.Id,
                cancellationToken);
        }

        await _botClient.AnswerCallbackQueryAsync(
            callbackQueryId: callbackQuery.Id,
            cancellationToken: cancellationToken);
    }

    private async Task HandleGroupSelection(
        long chatId,
        string groupId,
        string callbackQueryId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _botClient.SendChatActionAsync(
                chatId: chatId,
                chatAction: ChatAction.Typing,
                cancellationToken: cancellationToken);

            // Получаем расписание для выбранной группы
            var schedule = await _scheduleParser.GetGroupScheduleAsync(groupId);

            if (string.IsNullOrEmpty(schedule))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Не удалось загрузить расписание для выбранной группы",
                    cancellationToken: cancellationToken);
                return;
            }

            // Отправляем форматированное расписание
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
        // Проверяем, что ввод может быть номером группы
        // Формат: буквы-цифры (РИЗ-220501 или РИЗ220501)
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
}