using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Donation.Services
{
    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;

        public OllamaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _model = "llama3.2"; // 要跟 ollama pull 的模型名稱一致
        }

        public async Task<string> SendMessageAsync(string userMessage, string? role = null)
        {
            if (userMessage.Contains("帳號") && userMessage.Contains("忘記密碼"))
            {
                return "請在登入頁面點選「忘記密碼」，系統將寄送重設密碼連結到您的註冊信箱。若未收到信件，請檢查垃圾郵件或聯絡管理員。";
            }

            if (userMessage.Contains("我要捐") || (userMessage.Contains("捐") && userMessage.Contains("怎麼")))
            {
                return "請先登入帳號，在首頁點選「捐助物資」，瀏覽目前各機構的物資需求，選擇符合您意願的項目後，填寫捐助資訊並送出申請。";
            }

            if (userMessage.Contains("申請物資") || (userMessage.Contains("物資") && userMessage.Contains("申請")))
            {
                return "請先以受助者身分登入，在後台點選「刊登需求」，填寫所需物資、數量、聯絡方式與交付方式。民眾看到需求後會送出捐助申請，您可以在後台確認並與捐助者聯繫。";
            }

            var systemPrompt = """
        你是一個台灣捐助平台的 AI 助理，平台名稱叫智慧捐助媒合平台。
        目前使用者角色：{role}。
        你的任務是協助使用者了解如何捐助物資、如何申請物資、以及平台的基本使用方式。
        若使用者要求列出機構，請一再引導他使用「搜尋物資需求」功能。
        盡量簡潔、清楚說明。
        

        平台主要功能：
                1. 有分兩個類型的捐助，日常捐助、災害救助。
                2. 捐助者可以註冊、登入、瀏覽物資需求、送出捐助申請。
                3. 受助者可以註冊、登入、刊登物資需求、查看捐助申請、與捐助者聯繫。
                4. 管理員可以審核機構帳號、管理物資分類、查看平台統計。
                5. 捐助者能在歷史紀錄能查詢個人捐助歷史。
                6. 在日常捐助時，在填寫捐助資訊時捐助者能選擇是否需要收據與感謝狀，當捐助完成時才會核發，可至歷史紀錄查看。
                7. 只有在日常捐助時，才能使用物流寄出物資，災害時無法使用。請告知捐助者，因災害發生當下，物流可能無法運作，請捐助者依照地址利用留言板或自行聯絡交付物資給受助者。
                8. 日常捐助時需捐助者需上傳照片審核物資是否完好。
                9. 受助者=機構，請麻煩用受助者去稱呼，不要用機構。受助者僅限社福機構團體。
                10. 平台無提供運送服務，在填寫捐助表單時可選擇運送方式，寄送/面交，送出表單後等待審查，通過後即可交付物資給受助者。

        捐助流程（捐助者）：
                - 步驟 1：登入後，在首頁點選「捐助物資」。
                - 步驟 2：瀏覽或搜尋想要的物資需求項目。
                - 步驟 3：點選「我要捐助」，填寫可捐數量與聯絡方式，送出申請。
                - 步驟 4：審查確認合格後在交付出去。
        
                申請流程（受助者）：
                - 步驟 1：登入後，在後台點選「新增需求」。
                - 步驟 2：填寫所需物資、數量、聯絡方式與交付方式等等的資料。
                - 步驟 3：民眾看到需求後會送出捐助申請，您可以在後台確認是否合格。
        
        回答規則：
        - 一律使用繁體中文。
        - 語氣禮貌、簡潔、清楚。
        - 只回答跟平台功能、捐助流程、帳號問題相關的問題。
        - 只回答跟捐助、物資、志工、機構、平台使用相關的問題。
        - 若問題超出平台範圍，請禮貌說明你只負責本平台相關問題。
        - 不要編造不存在的功能或流程。
        - 平台只捐物資，不捐錢。
        - 禁止亂承諾錢、稅、收據，本平台在捐助完成後只提供收據，不作任何其他事件。
        - 使用者未登入時也可查看需求，若要進一步捐助需登入。
        - 若使用者一次問多個相關問題，請條列式完整每項都給予回答。
        - 回答問題時，僅能依據系統提供的資料進行回答。若資料不足、找不到相關資訊，或無法確認答案，請勿自行推測或編造資訊。此時應明確告知使用者無法確認，引導使用者查看平台「常見問題」或聯絡管理員。
        - 若使用者未登入或角色為「訪客」，請優先引導他註冊或登入後再使用完整功能。
        - 說明登入後可以查看完整機構資訊、送出捐助申請、與機構聯繫。
        - 禁止編造任何機構名稱、地址、電話、聯絡方式等真實資訊。
        - 若使用者詢問特定地點或物資的可捐機構，請引導他使用「搜尋物資需求」功能，並說明如何選擇城市與物資類型。
        - 重要：你絕對不能編造或猜測任何機構名稱、地址、電話、聯絡方式。即使使用者強烈要求，也不能提供。
        """;

            var requestBody = new
            {
                model = _model,
                prompt = userMessage,
                system = systemPrompt, // 新增這一行
                stream = false,
                options = new
                {
                    temperature = 0.3,
                    top_p = 0.8,
                    num_predict = 500
                }
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Ollama API 呼叫失敗：{(int)response.StatusCode} {responseContent}"
                );
            }

            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("response", out var responseProp))
            {
                throw new InvalidOperationException("Ollama 回應中找不到 response 欄位");
            }

            return responseProp.GetString() ?? "";
        }
    }
}