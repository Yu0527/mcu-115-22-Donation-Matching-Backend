using Microsoft.AspNetCore.Mvc;
using Donation.Services;

namespace Donation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssistantController : ControllerBase
    {
        private readonly IOpenRouterService _openRouterService;

        public AssistantController(IOpenRouterService openRouterService)
        {
            _openRouterService = openRouterService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                Console.WriteLine("沒有收到 message，或 message 是空白。");
                return BadRequest(new { message = "message 不能為空白" });
            }

            Console.WriteLine($"收到前端訊息：{request.Message}");
            Console.WriteLine($"收到前端角色：{request.Role}");
            
            var history = request.History ?? new List<ChatHistoryItem>();
            var role = string.IsNullOrWhiteSpace(request.Role) ? "訪客" : request.Role.Trim();

            Console.WriteLine($"處理後角色：{role}");
            Console.WriteLine("準備呼叫 OpenRouter 雲端 AI...");

            try
            {
                var answer = await _openRouterService.SendMessageAsync(request.Message.Trim(), role, history);

                Console.WriteLine($"OpenRouter 回覆內容：{answer}");

                return Ok(new
                {
                    answer = answer
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("OpenRouter 呼叫失敗：");
                Console.WriteLine(ex.ToString());

                return StatusCode(500, new
                {
                    message = "AI 助理目前無法使用。",
                    detail = ex.Message
                });
            }
        }

        public class ChatRequest
        {
            public string Message { get; set; }
            public string Role { get; set; }
            public List<ChatHistoryItem> History { get; set; } = new();
        }

        public class ChatHistoryItem
        {
            public string Sender { get; set; } = "";
            public string Content { get; set; } = "";
        }
    }
}