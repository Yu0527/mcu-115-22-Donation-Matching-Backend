using Donation.Controllers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Donation.Services
{
    public interface IOpenRouterService
    {
        Task<string> SendMessageAsync(
            string userMessage,
            string? role = null,
            List<AssistantController.ChatHistoryItem>? history = null,
            string? platformBrief = null);
    }

    public class OpenRouterService : IOpenRouterService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenRouterService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _apiKey = configuration["OpenRouter:ApiKey"] ?? throw new InvalidOperationException("找不到 OpenRouter API Key 設定。");
            _model = "google/gemini-2.5-flash";
        }

        public async Task<string> SendMessageAsync(
            string userMessage,
            string? role = null,
            List<AssistantController.ChatHistoryItem>? history = null,
            string? platformBrief = null)
        {
            if (userMessage.Contains("帳號") && userMessage.Contains("忘記密碼"))
            {
                return "請在登入頁面點選「忘記密碼」，系統會寄重設連結到註冊信箱。若未收到，請先檢查垃圾郵件，或到「聯絡我們」留下信箱。";
            }

            if (userMessage.Contains("捐錢") || userMessage.Contains("捐款") || userMessage.Contains("現金") || userMessage.Contains("金錢") || userMessage.Contains("匯款") || userMessage.Contains("轉帳"))
            {
                return "本平台只媒合實體物資，不接受金錢、匯款、轉帳或刷卡。你可以到「日常捐助」或開啟中的「災害救助」查看需求後送出捐助。";
            }

            if (userMessage.Contains("我要捐") || userMessage.Contains("我想捐") || userMessage.Contains("想捐助") || userMessage.Contains("如何捐") || userMessage.Contains("怎麼捐"))
            {
                return "請先登入捐助者帳號，進入「日常捐助」或「災害救助」選需求，填寫實際物資與交付方式後送出。通過審核後再依寄送或面交自行交付。";
            }

            if (userMessage.Contains("申請物資") || (userMessage.Contains("物資") && userMessage.Contains("申請")))
            {
                return "請先以受助者身分登入，在後台刊登需求並填寫數量與聯絡方式。捐助者送出申請後，你可在審核頁接受或不接受。";
            }

            var currentRole = string.IsNullOrWhiteSpace(role) ? "訪客" : role.Trim();
            var brief = string.IsNullOrWhiteSpace(platformBrief) ? DefaultPlatformBrief : platformBrief.Trim();

            var systemPrompt = $"""
[Identity]
你是「智慧捐助媒合平台」的客服助理。
目前使用者角色：{currentRole}。

[Tone & Language]
1. 必須使用臺灣繁體中文。禁止簡體中文。
2. 語氣平實、直接。回答最多 3 句，必要時用短步驟。
3. 只輸出給使用者看的最終答案，禁止輸出思考過程。

[Platform Facts]
{brief}

[Behavioral Rules]
1. 「不會用」「太複雜」「下一步呢」「找不到」等追問，要依對話與角色繼續協助，不可直接判定無關。
2. 明確與平台無關的問題，才回覆：抱歉，我僅能協助智慧捐助媒合平台相關問題，例如帳號、物資捐助、物資需求與平台操作。
3. 只能依本提示詞回答；資料不足就說無法確認，並建議查看「常見問題」或「聯絡我們」。
4. 不得編造受助者名稱、地址、電話、平台按鈕或政策。
5. 不要把協助人力稱為「志工」；正式稱呼是「協力者」。
6. 不要談政治、政黨或候選人。
""";

            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt }
            };

            if (history != null)
            {
                foreach (var item in history.TakeLast(6))
                {
                    if (string.IsNullOrWhiteSpace(item.Content))
                    {
                        continue;
                    }

                    var chatRole = item.Sender == "assistant" ? "assistant" : "user";
                    messages.Add(new { role = chatRole, content = item.Content.Trim() });
                }
            }

            messages.Add(new
            {
                role = "user",
                content = $"""
                【輸出規則】
                必須使用臺灣繁體中文。
                只輸出最終答案，禁止思考過程。
                回答最多 3 句。

                問題：{userMessage}
                """
            });

            var requestBody = new
            {
                model = _model,
                messages = messages,
                max_tokens = 512,
                temperature = 0.2
            };

            var json = JsonSerializer.Serialize(requestBody);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://localhost");
                _httpClient.DefaultRequestHeaders.Add("X-Title", "Smart Donation Platform");

                using var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[OpenRouter 錯誤回應] {(int)response.StatusCode}: {responseContent}");
                    throw new InvalidOperationException($"OpenRouter API 錯誤代碼: {(int)response.StatusCode}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                return content?.Trim() ?? "無法取得 AI 回覆。";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OpenRouter 例外攔截] {ex.Message}");
                return "AI 助理目前忙線中或連線異常，請稍後再試。若需即時協助，請查看常見問題或聯絡我們。";
            }
        }

        private const string DefaultPlatformBrief = """
平台只媒合實體物資，沒有金錢捐助、匯款或代收款。

留言：日常捐助與災害物資的需求詳情頁下方有留言區。登入後可以對該筆需求留言、回覆與刪除自己的留言。留言不是客服單，也不是會員收件匣。

訊息：會員專區的「收件匣」才是站內信。捐助者可與已媒合的受助機構通信，包含審核結果通知。收件匣不是公開留言板。

物流：機構審核通過、而且捐助方式是寄送時，捐助者到「捐助紀錄」自己選擇物流公司並上傳單號。機構不能代填。上傳後，「配送查詢」只查這個平台上該捐助者自己的單號，進度只有兩種平台狀態：「已寄出，等待受助機構簽收」或「受助機構已簽收」。這裡沒有物流商的即時貨態。最新貨態要到該物流公司官網查。

感謝牆：每一筆捐助可在捐助紀錄改成「公開」或「只有自己看得到」，名稱可選匿名或全名。只有已上傳物流單號、且設為公開的寄送捐助，才會出現在感謝牆。

災害救助：管理者可開啟或關閉。關閉時，大廳仍可看到物資捐助、協力需求、地圖三個入口，但點進去會提示目前已關閉，不能送出捐助或報名。開啟時，地圖上的點是目前上架的協力需求地點，不是「捐助地圖」裡的受助機構。

協力者：受助機構在災害期間發布需要人手的工作，例如搬運、整理、炊事、引導。民眾是來幫忙的協力者，不是依法定志願服務運用的志工，平台也不核發志工證。
""";
    }
}
