using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Donation.Repositories
{
    public class AiDonationRepository
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _serviceRoleKey;
        private readonly ILogger<AiDonationRepository> _logger;

        private const string DonationTable = "donor_daily_donations";

        public AiDonationRepository(
            HttpClient httpClient,
            string baseUrl,
            string serviceRoleKey,
            ILogger<AiDonationRepository> logger)
        {
            _httpClient = httpClient;
            _baseUrl = baseUrl;
            _serviceRoleKey = serviceRoleKey;
            _logger = logger;
        }

        public async Task UpdateAiReviewResultAsync(
            string donationId,
            string status,
            string aiDecision,
            string aiCondition,
            string aiReason,
            DateTimeOffset aiCheckedAt,
            CancellationToken cancellationToken)
        {
            string url = TrimTrailingSlash(_baseUrl) +
                         "/rest/v1/" + DonationTable +
                         "?id=eq." + Uri.EscapeDataString(donationId);

            var payload = new
            {
                status,
                ai_decision = aiDecision,
                ai_condition = aiCondition,
                ai_reason = aiReason,
                ai_checked_at = aiCheckedAt.ToString("O")
            };

            using HttpRequestMessage request = new(HttpMethod.Patch, url);
            AddSupabaseHeaders(request, _serviceRoleKey);
            request.Headers.Add("Prefer", "return=minimal");
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );

            using HttpResponseMessage response =
                await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Supabase 寫入 AI 審核結果失敗。DonationId: {DonationId}, StatusCode: {StatusCode}",
                    donationId,
                    response.StatusCode
                );
                throw new InvalidOperationException("寫入 AI 審核結果失敗。");
            }

            _logger.LogInformation(
                "AI 審核結果已寫入 Supabase。DonationId: {DonationId}, AiDecision: {AiDecision}",
                donationId,
                aiDecision
            );
        }

        private static void AddSupabaseHeaders(
            HttpRequestMessage request,
            string serviceRoleKey)
        {
            request.Headers.Add("apikey", serviceRoleKey);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        }

        private static string TrimTrailingSlash(string value) =>
            value.TrimEnd('/');
    }
}
