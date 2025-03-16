using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class ScheduleParser
{
    static async Task Main()
    {
        try
        {
            string endDate = "2025-03-23";
            string startDate = "2025-03-17";
            string groupID = "59774";

            string url = $"https://urfu.ru/api/v2/schedule/groups/{groupID}/schedule?date_gte={startDate}&date_lte={endDate}";

            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();

            // Десериализация JSON
            using JsonDocument doc = JsonDocument.Parse(responseBody);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("events", out JsonElement events))
            {
                foreach (JsonElement eventElement in events.EnumerateArray())
                {
                    string title = eventElement.GetProperty("title").GetString() ?? "Нет данных";
                    string date = eventElement.GetProperty("date").GetString() ?? "Нет данных";
                    string timeBegin = eventElement.GetProperty("timeBegin").GetString() ?? "Нет данных";
                    string timeEnd = eventElement.GetProperty("timeEnd").GetString() ?? "Нет данных";
                    string teacherName = eventElement.GetProperty("teacherName").GetString() ?? "Нет данных";

                    Console.WriteLine($"Предмет: {title} {date} {timeBegin} {timeEnd} {teacherName}");
                }
            }
            else
            {
                Console.WriteLine("Поле 'events' отсутствует в JSON-ответе.");
            }
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"Ошибка HTTP-запроса: {e.Message}");
        }
        catch (JsonException e)
        {
            Console.WriteLine($"Ошибка обработки JSON: {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Произошла ошибка: {e.Message}");
        }
    }
}