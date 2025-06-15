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

            return new[] { InlineKeyboardButton.WithCallbackData(teacher.Name, $"teacher_{teacher.Id}_{teacher.Name}") };
        })
        .Where(button => button != null)
        .ToList();

        return buttons.Count > 0 ? new InlineKeyboardMarkup(buttons) : null;
    }

    public async Task<string?> GetGroupScheduleAsync(string groupId, string groupTitle, DateTime startDate, DateTime endDate)
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
            return FormatGroupSchedule(json, groupTitle, startDate, endDate);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetTeacherScheduleAsync(string teacherId, string teacherName, DateTime startDate, DateTime endDate)
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
            return FormatTeacherSchedule(json, teacherName, startDate, endDate);
        }
        catch
        {
            return null;
        }
    }

    private string FormatGroupSchedule(string json, string groupTitle, DateTime startDate, DateTime endDate)
    {
        try
        {
            var scheduleData = JsonConvert.DeserializeObject<ScheduleResponse>(json);
            if (scheduleData?.Events == null || !scheduleData.Events.Any())
                return "Расписание не найдено";

            List<Lesson> orderedList = scheduleData.Events.OrderBy(l => l.Date).ThenBy(l => l.TimeBegin).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📆 Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
            sb.AppendLine($"📚 Группа: {groupTitle}");

            foreach (var lesson in orderedList)
            {
                if (orderedList.IndexOf(lesson) == 0 || lesson.Date != orderedList[orderedList.IndexOf(lesson) - 1].Date)
                {
                    sb.AppendLine($"\n<b>📌 {lesson.Date:dddd, dd.MM.yyyy}</b>");
                }

                sb.AppendLine($"\n🕒 <i>{lesson.TimeBegin:hh\\:mm} - {lesson.TimeEnd:hh\\:mm}</i>");
                sb.AppendLine($"   <b>{lesson.Title}</b>");

                if (!string.IsNullOrEmpty(lesson.TeacherName) &&
                    (orderedList.IndexOf(lesson) == 0 || lesson.TeacherName != orderedList[orderedList.IndexOf(lesson) - 1].TeacherName))
                    sb.AppendLine($"   👨‍🏫 {lesson.TeacherName}");

                if (!string.IsNullOrEmpty(lesson.AuditoryTitle) && !string.IsNullOrEmpty(lesson.AuditoryLocation) && lesson.AuditoryTitle != lesson.AuditoryLocation)
                    sb.AppendLine($"   🚪 {lesson.AuditoryLocation}, каб. {lesson.AuditoryTitle}");
                else if (!string.IsNullOrEmpty(lesson.AuditoryTitle))
                    sb.AppendLine($"   🚪 {lesson.AuditoryTitle}");

                if (!string.IsNullOrEmpty(lesson.LoadType))
                    sb.AppendLine($"   � Тип: {lesson.LoadType}");
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

    private string FormatTeacherSchedule(string json, string teacherName, DateTime startDate, DateTime endDate)
    {
        try
        {
            var scheduleData = JsonConvert.DeserializeObject<ScheduleResponse>(json);
            if (scheduleData?.Events == null || !scheduleData.Events.Any())
                return "Расписание не найдено";

            List<Lesson> orderedList = scheduleData.Events.OrderBy(l => l.Date).ThenBy(l => l.TimeBegin).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"📆 Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
            sb.AppendLine($"👨‍🏫 Преподаватель: {teacherName}");

            foreach (var lesson in orderedList)
            {
                if (orderedList.IndexOf(lesson) == 0 || lesson.Date != orderedList[orderedList.IndexOf(lesson) - 1].Date)
                {
                    sb.AppendLine($"\n<b>📌 {lesson.Date:dddd, dd.MM.yyyy}</b>");
                }

                sb.AppendLine($"\n🕒 <i>{lesson.TimeBegin:hh\\:mm} - {lesson.TimeEnd:hh\\:mm}</i>");
                sb.AppendLine($"   <b>{lesson.Title}</b>");
                sb.AppendLine($"   👥 Группа: {lesson.GroupName}");

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

    private class ScheduleResponse
    {
        public List<Lesson> Events { get; set; } = new();
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
        public string GroupName { get; set; } = string.Empty;
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