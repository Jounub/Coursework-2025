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

    private readonly Dictionary<long, UserState> _userStates = new();
    private enum UserState
    {
        None,
        AwaitingGroupSearch,
        AwaitingTeacherSearch
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

        if (messageText.StartsWith("/start"))
        {
            await SendWelcomeMessage(chatId, cancellationToken);
            return;
        }

        if (messageText.Equals("/search", StringComparison.OrdinalIgnoreCase))
        {
            await SendSearchOptions(chatId, cancellationToken);
            return;
        }

        if (_userStates.TryGetValue(chatId, out var state))
        {
            _userStates.Remove(chatId);

            if (state == UserState.AwaitingGroupSearch)
            {
                await HandleGroupSearch(chatId, messageText, cancellationToken);
            }
            else if (state == UserState.AwaitingTeacherSearch)
            {
                await HandleTeacherSearch(chatId, messageText, cancellationToken);
            }
            return;
        }

        if (IsPotentialGroupName(messageText))
        {
            await HandleGroupSearch(chatId, messageText, cancellationToken);
            return;
        }

        if (IsPotentialTeacherName(messageText))
        {
            await HandleTeacherSearch(chatId, messageText, cancellationToken);
            return;
        }

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Используйте /start для выбора типа поиска или введите номер группы/ФИО преподавателя",
            cancellationToken: cancellationToken);
    }

    private async Task SendWelcomeMessage(long chatId, CancellationToken ct)
    {
        var welcomeText = @"📚 <b>Бот расписания УрФУ</b>

Для поиска используйте:
/search - выбор типа поиска
[номер группы] - прямой поиск группы
[ФИО преподавателя] - прямой поиск преподавателя";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: welcomeText,
            parseMode: ParseMode.Html,
            cancellationToken: ct);
    }

    private async Task SendSearchOptions(long chatId, CancellationToken ct)
    {
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🔍 Поиск группы", "search_group"),
                InlineKeyboardButton.WithCallbackData("👨‍🏫 Поиск преподавателя", "search_teacher")
            }
        });

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Выберите тип поиска:",
            replyMarkup: keyboard,
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
            _logger.LogError(ex, "Group search error");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при поиске группы. Попробуйте позже.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleTeacherSearch(
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

            var keyboard = await _scheduleParser.SearchTeachersAsync(searchQuery);

            if (keyboard == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Преподаватели не найдены. Проверьте правильность запроса.",
                    cancellationToken: cancellationToken);
                return;
            }

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🔍 Найденные преподаватели:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teacher search error");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при поиске преподавателя. Попробуйте позже.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(
        CallbackQuery callbackQuery,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.Data == "search_group")
        {
            _userStates[callbackQuery.Message!.Chat.Id] = UserState.AwaitingGroupSearch;
            await _botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                text: "🔍 Введите номер группы (например, РИЗ-220501):",
                cancellationToken: cancellationToken);
        }
        else if (callbackQuery.Data == "search_teacher")
        {
            _userStates[callbackQuery.Message!.Chat.Id] = UserState.AwaitingTeacherSearch;
            await _botClient.SendTextMessageAsync(
                chatId: callbackQuery.Message.Chat.Id,
                text: "🔍 Введите ФИО преподавателя:",
                cancellationToken: cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("group_"))
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
        else if (callbackQuery.Data!.StartsWith("tchr_"))
        {
            var parts = callbackQuery.Data.Split('_');
            string teacherId = parts[1];
            var teacherName = parts[2];
            await HandleTeacherSelection(
                callbackQuery.Message!.Chat.Id,
                teacherId,
                teacherName,
                callbackQuery.Id,
                cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("month_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var id = parts[1];
            var month = int.Parse(parts[2]);
            //var name = parts[3];
            var isTeacher = false;
            await SendDaySelection(
                callbackQuery.Message!.Chat.Id,
                id,
                //name,
                month,
                isTeacher,
                cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("teacher_month_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var id = parts[2];
            var month = int.Parse(parts[3]);
            //var name = parts[4];
            var isTeacher = true;
            await SendDaySelection(
                callbackQuery.Message!.Chat.Id,
                id,
                //name,
                month,
                isTeacher,
                cancellationToken);
        }
        else if (callbackQuery.Data!.StartsWith("day_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var groupId = parts[1];
            var month = int.Parse(parts[2]);
            var day = int.Parse(parts[3]);
            var oneDay = int.Parse(parts[4]);
            //var groupTitle = parts[5];
            var selectedDate = new DateTime(DateTime.Now.Year, month, day);
            bool oneDaySchedule = Convert.ToBoolean(oneDay);
            await GetWeekSchedule(
                callbackQuery.Message!.Chat.Id,
                groupId,
                //groupTitle,
                selectedDate,
                cancellationToken,
                oneDaySchedule);
        }
        else if (callbackQuery.Data!.StartsWith("teacher_day_"))
        {
            var parts = callbackQuery.Data.Split('_');
            var teacherId = parts[2];
            var month = int.Parse(parts[3]);
            var day = int.Parse(parts[4]);
            var oneDay = int.Parse(parts[5]);
            //var teacherName = parts[6];
            var selectedDate = new DateTime(DateTime.Now.Year, month, day);
            bool oneDaySchedule = Convert.ToBoolean(oneDay);
            await GetTeacherWeekSchedule(
                callbackQuery.Message!.Chat.Id,
                teacherId,
                //teacherName,
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
        //string groupTitle,
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
            DateTime endOfWeek = oneDaySchedule ? startOfWeek : startOfWeek.AddDays(6);

            var schedule = await _scheduleParser.GetGroupScheduleAsync(groupId, startOfWeek, endOfWeek);

            if (schedule == null || !schedule.Any())
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Не удалось загрузить расписание для выбранной группы и недели",
                    cancellationToken: cancellationToken);
                return;
            }

            foreach (var part in schedule)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: part,
                    parseMode: ParseMode.Html,
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Group schedule load error");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при загрузке расписания группы",
                cancellationToken: cancellationToken);
        }
    }

    private async Task GetTeacherWeekSchedule(
        long chatId,
        string teacherId,
        //string teacherName,
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
            DateTime endOfWeek = oneDaySchedule ? startOfWeek : startOfWeek.AddDays(6);

            var schedule = await _scheduleParser.GetTeacherScheduleAsync(teacherId, startOfWeek, endOfWeek);

            if (string.IsNullOrEmpty(schedule))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Не удалось загрузить расписание для выбранного преподавателя",
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
            _logger.LogError(ex, $"Teacher schedule load error");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при загрузке расписания преподавателя",
                cancellationToken: cancellationToken);
        }
    }

    private bool IsPotentialGroupName(string input)
    {
        return input.Length >= 5 &&
               input.Any(char.IsLetter) &&
               input.Any(char.IsDigit);
    }

    private bool IsPotentialTeacherName(string input)
    {
        return input.Any(char.IsWhiteSpace) &&
               input.Any(char.IsLetter) &&
               input.Length >= 5;
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
            await SendMonthSelection(chatId, groupId, groupTitle, false, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in group selection for {groupTitle}");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при выборе группы",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleTeacherSelection(
        long chatId,
        string teacherId,
        string teacherName,
        string callbackQueryId,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendMonthSelection(chatId, teacherId, teacherName, true, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in teacher selection for {teacherName}");
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "⚠ Ошибка при выборе преподавателя",
                cancellationToken: cancellationToken);
        }
    }

    private async Task SendMonthSelection(
        long chatId,
        string id,
        string name,
        bool isTeacher,
        CancellationToken ct)
    {
        var currentDate = DateTime.Now;
        var buttons = new List<InlineKeyboardButton[]>();

        for (int i = 1; i <= 12; i++)
        {
            var monthName = new DateTime(currentDate.Year, i, 1).ToString("MMMM");
            var prefix = isTeacher ? "teacher_month" : "month";
            buttons.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    monthName,
                    $"{prefix}_{id}_{i}")
            });
        }

        var keyboard = new InlineKeyboardMarkup(buttons);
        var text = isTeacher
            ? $"Выберите месяц для преподавателя {name}:"
            : $"Выберите месяц для группы {name}:";

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: text,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task SendDaySelection(
        long chatId,
        string id,
        //string name,
        int month,
        bool isTeacher,
        CancellationToken ct)
    {
        var currentDate = DateTime.Now;
        var daysInMonth = DateTime.DaysInMonth(currentDate.Year, month);

        var buttons = new List<InlineKeyboardButton[]>();
        var row = new List<InlineKeyboardButton>();

        for (int day = 1; day <= daysInMonth; day++)
        {
            var prefix = isTeacher ? "teacher_day" : "day";
            row.Add(InlineKeyboardButton.WithCallbackData(
                day.ToString(),
                $"{prefix}_{id}_{month}_{day}_0"));

            if (row.Count == 7 || day == daysInMonth)
            {
                buttons.Add(row.ToArray());
                row.Clear();
            }
        }

        var todayPrefix = isTeacher ? "teacher_day" : "day";
        var todayText = isTeacher ? "Сегодня (преподаватель)" : "Сегодня";
        var tomorrowText = isTeacher ? "Завтра (преподаватель)" : "Завтра";

        row.Add(InlineKeyboardButton.WithCallbackData(
            todayText,
            $"{todayPrefix}_{id}_{DateTime.Today.Month}_{DateTime.Today.Day}_1"));

        row.Add(InlineKeyboardButton.WithCallbackData(
            tomorrowText,
            $"{todayPrefix}_{id}_{DateTime.Today.Month}_{DateTime.Today.AddDays(1).Day}_1"));

        buttons.Add(row.ToArray());

        var keyboard = new InlineKeyboardMarkup(buttons);

        await _botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Выберите день в {new DateTime(currentDate.Year, month, 1).ToString("MMMM")}:",
            replyMarkup: keyboard,
            cancellationToken: ct);
    }
}