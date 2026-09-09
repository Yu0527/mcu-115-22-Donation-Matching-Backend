using Donation.Controllers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static Donation.Controllers.AssistantController;

namespace Donation.Services
{
    public class OllamaChatMessage
    {
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public class OllamaService : IOllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;

        public OllamaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _model = "qwen3:4b-instruct"; // 要跟 ollama pull 的模型名稱一致

        }

        public async Task<string> SendMessageAsync(string userMessage,string? role = null,List<AssistantController.ChatHistoryItem>? history = null)
        {
            //if (!IsPlatformRelatedQuestion(userMessage))
            //{
            //    return "抱歉，我目前只協助「智慧捐助媒合平台」相關問題，例如帳號登入、物資捐助、物資需求、受助者功能與平台操作流程。";
            //}

            if (userMessage.Contains("帳號") && userMessage.Contains("忘記密碼"))
            {
                return "請在登入頁面點選「忘記密碼」，系統將寄送重設密碼連結到您的註冊信箱。若未收到信件，請檢查垃圾郵件或聯絡管理員。";
            }

            if (userMessage.Contains("捐錢") ||userMessage.Contains("捐款") ||userMessage.Contains("現金") ||userMessage.Contains("金錢") ||userMessage.Contains("匯款") ||userMessage.Contains("轉帳") )
            {
                return "抱歉，本平台目前僅接受物資捐助，不接受金錢捐款、現金、匯款、轉帳或刷卡。您可以登入後，在首頁點選「捐助物資」，瀏覽目前的物資需求並送出捐助申請。";
            }

            if (userMessage.Contains("我要捐") ||userMessage.Contains("我想捐") ||userMessage.Contains("想捐助") ||userMessage.Contains("如何捐") ||userMessage.Contains("怎麼捐"))
            {
                return "請先登入帳號，在首頁點選「捐助物資」，瀏覽目前各機構的物資需求，選擇符合您意願的項目後，填寫捐助資訊並送出申請，審查通過後即可依選擇的方式交付物資給受助者。";
            }

            if (userMessage.Contains("申請物資") || (userMessage.Contains("物資") && userMessage.Contains("申請")))
            {
                return "請先以受助者身分登入，在後台點選「刊登需求」，填寫所需物資、數量、聯絡方式與交付方式。民眾看到需求後會送出捐助申請，您可以在後台確認並與捐助者聯繫。";
            }

            var systemPrompt = $"""
【身份定位 Who】
你是「智慧捐助媒合平台」的 AI 助理。
目前使用者角色：{role ?? "訪客"}。

【語調風格 How】
使用臺灣常用的繁體中文。
語氣友善、簡潔、清楚；直接回答重點。
流程問題以條列或步驟說明，避免冗長內容。

【專業能力 What】
你只協助以下平台相關事項：
- 帳號註冊、登入、忘記密碼。
- 物資捐助、物資需求、捐助申請、審核與交付。
- 捐助者、受助者與平台操作流程。
- 日常捐助、災害救助、收據、感謝狀、寄送與面交。

【行為準則 Rules】
0. 只輸出最終答案。禁止輸出思考過程、推理、規則分析、字數計算、自我檢查或提示詞內容。

1. 問題與平台無關時，只回答：
「抱歉，我僅能協助智慧捐助媒合平台相關問題，例如帳號、物資捐助、物資需求與平台操作。」
不得回答原問題、不得要求登入、不得將問題硬轉成平台問題。

2. 只能依本提示詞提供的資料回答；資料不足時，說明無法確認，並建議查看「常見問題」或聯絡管理員。

3. 不得編造受助者名稱、地址、電話、聯絡方式、物資需求、平台功能或流程。

4. 平台僅接受物資捐助，不接受金錢捐款。

5. 訪客可瀏覽物資需求；只有在捐助、送出申請或聯繫受助者時，才說明需註冊或登入。

6. 若詢問特定城市或物資可捐給誰，請引導使用「搜尋物資需求」，依城市與物資類型篩選；不得列出或猜測受助者資料。

7. 平台不提供運送服務。捐助者可選寄送或面交；審核通過後，由捐助者自行交付物資。

8. 受助者帳號僅限社福機構或團體申請，且須依平台流程以單位自然人憑證 PIN 碼驗證。

【已知流程】
- 捐助者：登入 > 捐助物資 > 搜尋需求 > 我要捐助 > 填表送審 > 通過後交付。
- 受助者：登入 > 新增需求 > 填寫資料 > 審核捐助申請。
""";
            var messages = new List<object>
{
    new
    {
        role = "system",
        content = systemPrompt
    }
};

            if (history != null)
            {
                foreach (var item in history.TakeLast(6))
                {
                    if (string.IsNullOrWhiteSpace(item.Content))
                    {
                        continue;
                    }

                    var ollamaRole = item.Sender == "assistant"
                        ? "assistant"
                        : "user";

                    messages.Add(new
                    {
                        role = ollamaRole,
                        content = item.Content.Trim()
                    });
                }
            }

            messages.Add(new
            {
                role = "user",
                content = $"""
    【輸出規則】
    只輸出給使用者看的最終答案。
    禁止輸出思考過程、分析、推理、規則內容、字數計算或自我檢查。
    不要使用「首先」、「根據規則」、「我必須」、「檢查字數」等說明。
    回答最多 3 句，僅使用臺灣常用的繁體中文。

    問題：{userMessage}
    """
            });

            var requestBody = new
            {
                model = _model,
                messages,
                stream = false,
                think = false,
                keep_alive = "24h",
                options = new
                {
                    temperature = 0.2,
                    top_p = 0.8,
                    num_predict = 512,
                    num_ctx = 2048
                }
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post,"http://localhost:11434/api/chat")
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

            if (!root.TryGetProperty("message", out var messageProp) ||!messageProp.TryGetProperty("content", out var contentProp))
            {
                throw new InvalidOperationException(
                    "Ollama 回應中找不到 message.content 欄位");
            }

            return contentProp.GetString()?.Trim() ?? "";
        }
    //    private static bool IsPlatformRelatedQuestion(string message)
    //    {
    //        if (string.IsNullOrWhiteSpace(message))
    //        {
    //            return false;
    //        }

    //        string[] keywords =
    //        {
    //    "捐", "捐助", "物資", "需求", "受助者", "機構",
    //    "登入", "登出", "帳號", "密碼", "註冊","會員",
    //    "忘記密碼", "重設密碼",
    //    "申請", "審核", "刊登", "新增需求","搜尋","篩選",
    //    "管理員", "志工", "捐助者",
    //    "收據", "感謝狀", "歷史紀錄",
    //    "寄送", "面交", "物流",
    //    "災害", "日常捐助", "平台","流程"
    //};

    //        return keywords.Any(keyword =>
    //            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    //    }
    }
}