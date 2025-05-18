#nullable enable

using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Net;
using System.Text;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace test.Services;

public class ScheduleParser
{
    private const string ApiUrl = "https://urfu.ru/api/v2/schedule/groups";

    public async Task<InlineKeyboardMarkup?> SearchGroupsAsync(string searchQuery)
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{ApiUrl}?search={Uri.EscapeDataString(searchQuery)}");

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var groups = JsonConvert.DeserializeObject<List<GroupInfo>>(json);

        if (groups == null || !groups.Any())
            return null;

        // Создаем кнопки (максимум 10 групп)
        var buttons = groups.Take(10).Select(group =>
        {
            if (group.Id == null || group.Title == null)
                return null;

            return new[] { InlineKeyboardButton.WithCallbackData(group.Title, $"group_{group.Id}") };
        })
        .Where(button => button != null)
        .ToList();

        return buttons.Count > 0 ? new InlineKeyboardMarkup(buttons) : null;
    }

    public async Task<string?> GetGroupScheduleAsync(string groupId, DateTime startDate, DateTime endDate)
    {
        if (string.IsNullOrEmpty(groupId))
            return null;

        var url = $"https://urfu.ru/api/v2/schedule/groups/{groupId}/schedule?" +
                  $"date_gte={startDate:yyyy-MM-dd}&date_lte={endDate:yyyy-MM-dd}";

        using var httpClient = new HttpClient();

        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return FormatSchedule(json);
        }
        catch
        {
            return null;
        }
    }

    private string FormatSchedule(string json)
{
    try
    {
        var scheduleData = JsonConvert.DeserializeObject<ScheduleResponse>(json);
        if (scheduleData?.Events == null || !scheduleData.Events.Any())
            return "Расписание не найдено";

        var sb = new StringBuilder();
        sb.AppendLine($"📅 <b>Расписание группы {scheduleData.GroupName}</b>");
        sb.AppendLine($"📆 Период: {scheduleData.StartDate:dd.MM.yyyy} - {scheduleData.EndDate:dd.MM.yyyy}");
        sb.AppendLine();

        foreach (var lesson in scheduleData.Events)
        {
            sb.AppendLine($"\n<b>📌 {lesson.Date:dddd, dd.MM.yyyy}</b>");

            if (lesson.Title == null || !lesson.Title.Any())
            {
                sb.AppendLine("   🎉 Выходной");
                continue;
            }

            sb.AppendLine($"\n🕒 <i>{lesson.TimeBegin} - {lesson.TimeEnd}</i>");
            sb.AppendLine($"   <b>{lesson.Title}</b>");
               
            if (!string.IsNullOrEmpty(lesson.TeacherName))
                sb.AppendLine($"   👨‍🏫 {lesson.TeacherName}");
                
            if (!string.IsNullOrEmpty(lesson.AuditoryTitle))
                sb.AppendLine($"   🚪 {lesson.AuditoryTitle}");
                
            if (!string.IsNullOrEmpty(lesson.LoadType))
                sb.AppendLine($"   🏷 Тип: {lesson.LoadType}");            
        }

        return sb.ToString();
    }
    catch (Exception ex)
    {
        return $"⚠ Ошибка форматирования расписания: {ex.Message}";
    }
}

// Модели для десериализации JSON
private class ScheduleResponse
{
    public string GroupName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<Lesson> Events { get; set; } = new();
}

private class DaySchedule
{
    public DateTime Date { get; set; }
    public string Title { get; set; } = string.Empty;
}

private class Lesson
{
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string LoadType { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string AuditoryTitle { get; set; } = string.Empty;
    public TimeSpan TimeBegin { get; set; }
    public TimeSpan TimeEnd { get; set; }
}

    private class GroupInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }
}