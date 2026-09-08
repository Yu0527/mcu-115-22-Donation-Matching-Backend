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
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _model = "gemma4:e2b"; // 要跟 ollama pull 的模型名稱一致

        }

        public async Task<string> SendMessageAsync(string userMessage, string? role = null)
        {
            if (!IsPlatformRelatedQuestion(userMessage))
            {
                return "抱歉，我目前只協助「智慧捐助媒合平台」相關問題，例如帳號登入、物資捐助、物資需求、受助者功能與平台操作流程。";
            }

            if (userMessage.Contains("帳號") && userMessage.Contains("忘記密碼"))
            {
                return "請在登入頁面點選「忘記密碼」，系統將寄送重設密碼連結到您的註冊信箱。若未收到信件，請檢查垃圾郵件或聯絡管理員。";
            }

            if (userMessage.Contains("捐錢") ||
    userMessage.Contains("捐款") ||
    userMessage.Contains("現金") ||
    userMessage.Contains("金錢") ||
    userMessage.Contains("匯款") ||
    userMessage.Contains("轉帳") ||
    userMessage.Contains("信用卡") ||
    userMessage.Contains("刷卡"))
            {
                return "抱歉，本平台目前僅接受物資捐助，不接受金錢捐款、現金、匯款、轉帳或刷卡。您可以登入後，在首頁點選「捐助物資」，瀏覽目前的物資需求並送出捐助申請。";
            }

            if (userMessage.Contains("我要捐") ||
    userMessage.Contains("我想捐") ||
    userMessage.Contains("想捐助") ||
    userMessage.Contains("如何捐") ||
    userMessage.Contains("怎麼捐"))
            {
                return "請先登入帳號，在首頁點選「捐助物資」，瀏覽目前各機構的物資需求，選擇符合您意願的項目後，填寫捐助資訊並送出申請，審查通過後即可依選擇的方式交付物資給受助者。";
            }

            if (userMessage.Contains("申請物資") || (userMessage.Contains("物資") && userMessage.Contains("申請")))
            {
                return "請先以受助者身分登入，在後台點選「刊登需求」，填寫所需物資、數量、聯絡方式與交付方式。民眾看到需求後會送出捐助申請，您可以在後台確認並與捐助者聯繫。";
            }

            var systemPrompt = $"""
你是「智慧捐助媒合平台」AI 助理。
使用者角色：{role ?? "訪客"}。

規則：
- 一律使用臺灣常用的繁體中文，禁止簡體中文。
- 僅回答平台帳號、物資捐助、物資需求、受助者與操作流程相關問題。
- 資料不足或無法確認時，明確說明無法確認，並引導使用者查看「常見問題」或聯絡管理員。
- 不得編造受助者名稱、地址、電話、聯絡方式、物資需求或平台功能。
- 平台僅接受物資捐助，不接受金錢捐款。
- 訪客可瀏覽物資需求；只有在需要捐助、送出申請或聯繫受助者時，才告知需登入或註冊。
- 查詢特定地點或物資時，請引導使用「搜尋物資需求」，依城市與物資類型篩選。
- 平台不提供運送服務；捐助者可選寄送或面交，審查通過後自行交付物資。
- 若問題與平台無關，僅說明你協助智慧捐助媒合平台相關問題，不得要求登入。

流程：
- 捐助者：登入 > 捐助物資 > 搜尋需求 > 我要捐助 > 填表送審 > 通過後交付。
- 受助者：登入 > 新增需求 > 填寫資料 > 審核捐助申請。
""";

            var requestBody = new
            {
                model = _model,
                prompt = $"""
    請直接回答使用者問題，不要分析、不要展示思考過程。
    回答最多 2 句、100 個中文字。
    只能使用臺灣繁體中文。

    問題：{userMessage}
    """,
                system = systemPrompt,
                stream = false,
                think = false,
                keep_alive = "1h",
                options = new
                {
                    temperature = 0.2,
                    top_p = 0.8,
                    num_predict = 512,
                    num_ctx = 2048
                }
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine("===== Ollama 原始回應 =====");
            Console.WriteLine(responseContent);
            Console.WriteLine("==========================");


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
        private static bool IsPlatformRelatedQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string[] keywords =
            {
        "捐", "捐助", "物資", "需求", "受助者", "機構",
        "登入", "登出", "帳號", "密碼", "註冊",
        "忘記密碼", "重設密碼",
        "申請", "審核", "刊登", "新增需求",
        "管理員", "志工", "捐助者",
        "收據", "感謝狀", "歷史紀錄",
        "寄送", "面交", "物流",
        "災害", "日常捐助",
        "搜尋物資需求", "平台","流程"
    };

            return keywords.Any(keyword =>
                message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }
}