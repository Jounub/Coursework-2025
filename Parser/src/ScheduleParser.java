import org.json.simple.JSONArray;
import org.json.simple.JSONObject;
import org.json.simple.parser.JSONParser;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.URI;

public class ScheduleParser {
    public static void main(String[] args) {
        try {
            String endDate = "2025-03-23";
            String startDate = "2025-03-17";
            String groupID = "59774";

            HttpClient client = HttpClient.newHttpClient();
            HttpRequest request = HttpRequest.newBuilder()
                    .uri(URI.create("https://urfu.ru/api/v2/schedule/groups/" + groupID + "/schedule?date_gte=" + startDate + "&date_lte=" + endDate))
                    .header("User-Agent", "Mozilla/5.0")
                    .GET()
                    .build();

            HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());

            //Лабораторная работа 10, Архипов Н.А.©, 1.3.3 "Как прочитать файл JSON"
            JSONParser parser = new JSONParser();
            Object obj = parser.parse(response.body());
            JSONObject jsonObject = (JSONObject) obj;

            JSONArray jsonArray = (JSONArray) jsonObject.get("events");

            for (Object o : jsonArray){
                JSONObject event = (JSONObject) o;
                System.out.print("Предмет: " + event.get("title") + " " + event.get("date") + " "
                        + event.get("timeBegin") + " " + event.get("timeEnd") + " "
                        + event.get("teacherName") + "\n");
            }
        } catch (Exception e) {
            e.printStackTrace();
        }
    }
}
