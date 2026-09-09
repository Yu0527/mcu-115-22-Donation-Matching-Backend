using Donation.Controllers;
using static Donation.Controllers.AssistantController;

namespace Donation.Services
{
    public interface IOllamaService
    {
        /// <summary>
        /// 傳送使用者訊息並取得 AI 回覆。
        /// </summary>
        /// <param name="userMessage">使用者輸入的訊息。</param>
        /// <param name="role">使用者角色。</param>
        /// <returns>AI 回覆的文字內容。</returns>
        Task<string> SendMessageAsync(string userMessage,string? role = null, List<AssistantController.ChatHistoryItem>? history = null);
    }
}