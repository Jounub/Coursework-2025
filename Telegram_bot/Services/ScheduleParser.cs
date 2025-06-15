#nullable enable

using Newtonsoft.Json;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;

namespace test.Services;

public class ScheduleParser
{
    private const string GroupsApiUrl = "https://urfu.ru/api/v2/schedule/groups";
    private const string TeachersApiUrl = "https://urfu.ru/api/v2/schedule/teachers";

    public async Task<InlineKeyboardMarkup?> SearchGroupsAsync(string searchQuery)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{GroupsApiUrl}?search={Uri.EscapeDataString(searchQuery)}");

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var groups = JsonConvert.DeserializeObject<List<GroupInfo>>(json);

        if (groups == null || !groups.Any())
            return null;

        var buttons = groups.Take(10).Select(group =>
        {
            if (group.Id == null || group.Title == null)
                return null;

            return new[] { InlineKeyboardButton.WithCallbackData(group.Title, $"group_{group.Id}_{group.Title}") };
        })
        .Where(button => button != null)
        .ToList();

        return buttons.Count > 0 ? new InlineKeyboardMarkup(buttons) : null;
    }

    public async Task<InlineKeyboardMarkup?> SearchTeachersAsync(string searchQuery)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{TeachersApiUrl}?search={Uri.EscapeDataString(searchQuery)}");

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var teachers = JsonConvert.DeserializeObject<List<TeacherInfo>>(json);

        if (teachers == null || !teachers.Any())
            return null;

        var buttons = teachers.Take(10).Select(teacher =>
        {
            if (teacher.Id == null || teacher.Name == null)
                return null;
            string name = ShortenFullName(teacher.Name);
            return new[] { InlineKeyboardButton.WithCallbackData(teacher.Name, $"tchr_{teacher.Id}_{name}") };
        })
        .Where(button => button != null)
        .ToList();

        return buttons.Count > 0 ? new InlineKeyboardMarkup(buttons) : null;
    }

    public async Task<List<string>?> GetGroupScheduleAsync(string groupId, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        var url = $"{GroupsApiUrl}/{groupId}/schedule?" +
                  $"date_gte={startDate:yyyy-MM-dd}&date_lte={endDate:yyyy-MM-dd}";

        using var httpClient = new HttpClient();

        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return FormatGroupSchedule(json, startDate, endDate);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetTeacherScheduleAsync(string teacherId, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrEmpty(teacherId))
            return null;

        var url = $"{TeachersApiUrl}/{teacherId}/schedule?" +
                  $"date_gte={startDate:yyyy-MM-dd}&date_lte={endDate:yyyy-MM-dd}";

        using var httpClient = new HttpClient();

        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return FormatTeacherSchedule(json, startDate, endDate);
        }
        catch
        {
            return null;
        }
    }

    private List<string> FormatGroupSchedule(string json, DateTime startDate, DateTime endDate)
    {
        try
        {
            var scheduleData = JsonConvert.DeserializeObject<ScheduleResponse>(json);
            if (scheduleData?.Events == null || !scheduleData.Events.Any())
                return new List<string> { "Расписание не найдено" };

            // Сортировка занятий по дате и времени
            List<Lesson> orderedList = scheduleData.Events.OrderBy(l => l.Date).ThenBy(l => l.TimeBegin).ToList();

            var resultMessages = new List<string>();
            var currentMessage = new StringBuilder();
            var currentDay = (DateTime?)null;
            var isFirstMessage = true;

            // Добавляем заголовок только к первому сообщению
            if (isFirstMessage)
            {
                currentMessage.AppendLine($"📆 Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
                currentMessage.AppendLine($"📆 Группа: {scheduleData.Group.Title}");
                isFirstMessage = false;
            }

            foreach (var lesson in orderedList)
            {
                var dayChanged = currentDay != lesson.Date;
                currentDay = lesson.Date;

                var dayContent = new StringBuilder();

                // Добавляем дату только если это новый день
                if (dayChanged)
                {
                    dayContent.AppendLine($"\n<b>📌 {lesson.Date:dddd, dd.MM.yyyy}</b>");
                }

                dayContent.AppendLine($"\n🕒 <i>{lesson.TimeBegin:hh\\:mm} - {lesson.TimeEnd:hh\\:mm}</i>");
                dayContent.AppendLine($"    <b>{lesson.PairNumber}. {lesson.Title}</b>");

                if (!string.IsNullOrEmpty(lesson.TeacherName) &&
                    (orderedList.IndexOf(lesson) == 0 ||
                     lesson.TeacherName != orderedList[orderedList.IndexOf(lesson) - 1].TeacherName))
                {
                    dayContent.AppendLine($"   👨‍🏫 {lesson.TeacherName}");
                }

                if (!string.IsNullOrEmpty(lesson.AuditoryTitle) &&
                    !string.IsNullOrEmpty(lesson.AuditoryLocation) &&
                    lesson.AuditoryTitle != lesson.AuditoryLocation)
                {
                    dayContent.AppendLine($"   🚪 {lesson.AuditoryLocation}, каб. {lesson.AuditoryTitle}");
                }
                else if (!string.IsNullOrEmpty(lesson.AuditoryTitle))
                {
                    dayContent.AppendLine($"   🚪 {lesson.AuditoryTitle}");
                }

                if (!string.IsNullOrEmpty(lesson.LoadType))
                    dayContent.AppendLine($"   🏷 Тип: {lesson.LoadType}");
                if (!string.IsNullOrEmpty(lesson.Comment))
                    dayContent.AppendLine($"   💬 {lesson.Comment}");

                // Проверяем, поместится ли весь день в текущее сообщение
                if (dayChanged && (currentMessage.Length + dayContent.Length > 4000))
                {
                    // Если текущее сообщение не пустое, сохраняем его
                    if (currentMessage.Length > 0)
                    {
                        resultMessages.Add(currentMessage.ToString());
                        currentMessage = new StringBuilder();
                        // Добавляем заголовок периода только к первому сообщению
                        currentMessage.AppendLine("Продолжение расписания:");
                    }
                    // Добавляем день в новое сообщение (даже если он один превышает лимит)
                    currentMessage.Append(dayContent);
                }
                else
                {
                    currentMessage.Append(dayContent);
                }
            }

            // Добавляем последнее сообщение, если оно не пустое
            if (currentMessage.Length > 0)
            {
                resultMessages.Add(currentMessage.ToString());
            }

            // Добавляем нумерацию сообщений
            if (resultMessages.Count > 1)
            {
                for (int i = 0; i < resultMessages.Count; i++)
                {
                    resultMessages[i] += $"\n\n📄 Страница {i + 1} из {resultMessages.Count}";
                }
            }
            return resultMessages;
        }
        catch (Exception ex)
        {
            return new List<string> { $"⚠ Ошибка форматирования расписания: {ex.Message}" };
        }
    }
    private string FormatTeacherSchedule(string json, DateTime startDate, DateTime endDate)
    {
        try
        {
            var scheduleData = JsonConvert.DeserializeObject<ScheduleResponse>(json);
            if (scheduleData?.Events == null || !scheduleData.Events.Any())
                return "Расписание не найдено";

            List<Lesson> orderedList = scheduleData.Events.OrderBy(l => l.Date).ThenBy(l => l.TimeBegin).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📆 Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
            sb.AppendLine($"👨‍🏫 Преподаватель: {scheduleData.Teacher.Name}");

            foreach (var lesson in orderedList)
            {
                if (orderedList.IndexOf(lesson) == 0 || lesson.Date != orderedList[orderedList.IndexOf(lesson) - 1].Date)
                {
                    sb.AppendLine($"\n<b>📌 {lesson.Date:dddd, dd.MM.yyyy}</b>");
                }

                sb.AppendLine($"\n🕒 <i>{lesson.TimeBegin:hh\\:mm} - {lesson.TimeEnd:hh\\:mm}</i>");
                sb.AppendLine($"    <b>{lesson.PairNumber}. {lesson.Title}</b>");
                sb.AppendLine($"   👥 Группа: {lesson.GroupTitle}");

                if (!string.IsNullOrEmpty(lesson.AuditoryTitle) && !string.IsNullOrEmpty(lesson.AuditoryLocation) && lesson.AuditoryTitle != lesson.AuditoryLocation)
                    sb.AppendLine($"   🚪 {lesson.AuditoryLocation}, каб. {lesson.AuditoryTitle}");
                else if (!string.IsNullOrEmpty(lesson.AuditoryTitle))
                    sb.AppendLine($"   🚪 {lesson.AuditoryTitle}");

                if (!string.IsNullOrEmpty(lesson.LoadType))
                    sb.AppendLine($"   🏷 Тип: {lesson.LoadType}");
                if (!string.IsNullOrEmpty(lesson.Comment))
                    sb.AppendLine($"   💬 {lesson.Comment}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"⚠ Ошибка форматирования расписания: {ex.Message}";
        }
    }

    public static string ShortenFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return fullName;

        var parts = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return fullName;

        var result = new StringBuilder(parts[0]); // Фамилия

        if (parts.Length > 1)
            result.Append(" ").Append(parts[1][0]).Append("."); // Имя (первая буква)

        if (parts.Length > 2)
            result.Append(parts[2][0]).Append("."); // Отчество (первая буква)

        return result.ToString();
    }

    private class ScheduleResponse
    {
        public List<Lesson> Events { get; set; } = new();
        public TeacherInfo Teacher { get; set; } = new();
        public GroupInfo Group { get; set; } = new();
    }

    private class Lesson
    {
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string LoadType { get; set; } = string.Empty;
        public string AuditoryTitle { get; set; } = string.Empty;
        public TimeSpan TimeBegin { get; set; }
        public TimeSpan TimeEnd { get; set; }
        public string TeacherName { get; set; } = string.Empty;
        public string AuditoryLocation { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string GroupTitle { get; set; } = string.Empty;
        public string PairNumber { get; set; } = string.Empty;
    }

    private class GroupInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private class TeacherInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}