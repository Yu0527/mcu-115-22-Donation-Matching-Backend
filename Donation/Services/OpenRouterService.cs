using Donation.Controllers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Donation.Services
{
    public interface IOpenRouterService
    {
        Task<string> SendMessageAsync(string userMessage, string? role = null, List<AssistantController.ChatHistoryItem>? history = null);
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
            
            // 這裡換成我們剛剛選好的免費模型
            _model = "inclusionai/ling-3.0-flash-vl:free"; 
        }

        public async Task<string> SendMessageAsync(string userMessage, string? role = null, List<AssistantController.ChatHistoryItem>? history = null)
{
    // --- 1. 保留組員寫好的強大防呆快捷回覆 ---
    if (userMessage.Contains("帳號") && userMessage.Contains("忘記密碼"))
    {
        return "請在登入頁面點選「忘記密碼」，系統將寄送重設密碼連結到您的註冊信箱。若未收到信件，請檢查垃圾郵件或聯絡管理員。";
    }

    if (userMessage.Contains("捐錢") || userMessage.Contains("捐款") || userMessage.Contains("現金") || userMessage.Contains("金錢") || userMessage.Contains("匯款") || userMessage.Contains("轉帳"))
    {
        return "抱歉，本平台目前僅接受物資捐助，不接受金錢捐款、現金、匯款、轉帳或刷卡。您可以登入後，在首頁點選「捐助物資」，瀏覽目前的物資需求並送出捐助申請。";
    }

    if (userMessage.Contains("我要捐") || userMessage.Contains("我想捐") || userMessage.Contains("想捐助") || userMessage.Contains("如何捐") || userMessage.Contains("怎麼捐"))
    {
        return "請先登入帳號，在首頁點選「捐助物資」，瀏覽目前各機構的物資需求，選擇符合您意願的項目後，填寫捐助資訊並送出申請，審查通過後即可依選擇的方式交付物資給受助者。";
    }

    if (userMessage.Contains("申請物資") || (userMessage.Contains("物資") && userMessage.Contains("申請")))
    {
        return "請先以受助者身分登入，在後台點選「刊登需求」，填寫所需物資、數量、聯絡方式與交付方式。民眾看到需求後會送出捐助申請，您可以在後台確認並與捐助者聯繫。";
    }

    // --- 2. 組裝 System Prompt（強化繁體中文強制令） ---
var systemPrompt = $"""
[Identity]
你是一個「智慧捐助媒合平台」的 AI 助理。
目前使用者角色：{role ?? "訪客"}。

[Tone & Language Rules]
1. MUST use Traditional Chinese (Taiwan) (例如：資訊、網頁、後台、寄送). ABSOLUTELY NO Simplified Chinese.
2. 語氣友善、簡潔、清楚，直接回答重點。
3. 流程問題請以條列或步驟說明。

[Scope of Assistance]
你只協助以下平台相關事項：
- 帳號註冊、登入、忘記密碼。
- 物資捐助、物資需求、捐助申請、審核與交付。
- 捐助者、受助者與平台操作流程。
- 日常捐助、災害救助、收據、感謝狀、寄送與面交。

[Behavioral Rules (CRITICAL)]
0. OUTPUT ONLY THE FINAL ANSWER. 絕對禁止輸出思考過程、推理、規則分析或自我檢查。
1. 若問題與平台無關，固定回覆：「抱歉，我僅能協助智慧捐助媒合平台相關問題，例如帳號、物資捐助、物資需求與平台操作。」
2. 只能依本提示詞提供的資料回答；資料不足時，說明無法確認，並建議查看「常見問題」或聯絡管理員。
3. 不得編造受助者名稱、地址、電話、聯絡方式、物資需求、平台功能或流程。
4. 平台僅接受物資捐助，不接受金錢捐款。
5. 訪客可瀏覽物資需求；只有在捐助、送出申請或聯繫受助者時，才說明需註冊或登入。
6. 平台不提供運送服務。捐助者可選寄送或面交；審核通過後，由捐助者自行交付物資。
7. 受助者帳號僅限社福機構或團體申請，且須依平台流程進行驗證。

[Known Workflow]
- 捐助者：登入 > 捐助物資 > 搜尋需求 > 我要捐助 > 填表送審 > 通過後交付。
- 受助者：登入 > 新增需求 > 填寫資料 > 審核捐助申請。
""";

    var messages = new List<object>
    {
        new { role = "system", content = systemPrompt }
    };

    if (history != null)
    {
        foreach (var item in history.TakeLast(6))
        {
            if (string.IsNullOrWhiteSpace(item.Content)) continue;
            var chatRole = item.Sender == "assistant" ? "assistant" : "user";
            messages.Add(new { role = chatRole, content = item.Content.Trim() });
        }
    }

    messages.Add(new
    {
        role = "user",
        content = $"""
        【輸出規則】
        必須使用臺灣繁體中文輸出。
        只輸出給使用者看的最終答案，禁止輸出思考過程與分析。
        回答最多 3 句。

        問題：{userMessage}
        """
    });

    var requestBody = new
    {
        model = _model,
        messages = messages
    };

    var json = JsonSerializer.Serialize(requestBody);

    // --- 3. 加入 Try-Catch 例外處理防護網 ---
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
            // 記錄錯誤到主控台，並主動拋出例外讓 catch 攔截
            Console.WriteLine($"[OpenRouter 錯誤回應] {(int)response.StatusCode}: {responseContent}");
            throw new InvalidOperationException($"OpenRouter API 錯誤代碼: {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return content?.Trim() ?? "無法取得 AI 回覆。";
    }
    catch (Exception ex)
    {
        // 當發生 429 塞車、網路斷線或 API 異常時，這裡會攔截並給予友善提示，保護後端不崩潰
        Console.WriteLine($"[OpenRouter 例外攔截] {ex.Message}");
        return "AI 助理目前忙線中或連線異常，請稍後再試！若需即時協助，請查看常見問題或聯絡管理員。";
    }
}
    }
}