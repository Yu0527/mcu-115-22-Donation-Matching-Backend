using Microsoft.AspNetCore.Mvc;
using Donation.Services;

namespace Donation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AssistantController : ControllerBase
    {
        private readonly IOllamaService _ollamaService;

        public AssistantController(IOllamaService ollamaService)
        {
            _ollamaService = ollamaService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {

            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                Console.WriteLine("沒有收到 message，或 message 是空白。");

                return BadRequest(new
                {
                    message = "message 不能為空白"
                });
            }

            Console.WriteLine($"收到前端訊息：{request.Message}");
            Console.WriteLine($"收到前端角色：{request.Role}");
            Console.WriteLine("===== 前端傳入 History =====");

            var history = request.History ?? new List<ChatHistoryItem>();

            Console.WriteLine($"History 數量：{history.Count}");

            for (var i = 0; i < history.Count; i++)
            {
                var item = history[i];

                Console.WriteLine(
                    $"History[{i}] Sender：{item.Sender}，Content：{item.Content}"
                );
            }

            Console.WriteLine("============================");

            var role = string.IsNullOrWhiteSpace(request.Role)
                ? "訪客"
                : request.Role.Trim();

            Console.WriteLine($"處理後角色：{role}");
            Console.WriteLine("準備呼叫 Ollama...");

            try
            {
                var answer = await _ollamaService.SendMessageAsync(request.Message.Trim(),role,history);

                Console.WriteLine($"Ollama 回覆內容：{answer}");

                return Ok(new
                {
                    answer = answer
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ollama 呼叫失敗：");
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