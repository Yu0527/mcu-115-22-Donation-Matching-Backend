using Donation.Domains;
using Donation.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Donation.Services
{
    public sealed class AiDonationReviewService
    {
        private const string DonationTable = "donor_daily_donations";
        private const string FilesTable = "donation_files";
        private const string DemandTable = "agency_daily_supply_items";
        private const string StorageBucket = "donation-files";

        private static readonly string[] AllowedConditions =
        {
            "全新", "二手", "有擦痕", "過期", "毀損", "無法判斷"
        };

        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiDonationReviewService> _logger;
        private readonly AiDonationRepository _donationRepository;

        public AiDonationReviewService(
            AiDonationRepository donationRepository,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<AiDonationReviewService> logger)
        {
            _donationRepository = donationRepository;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<AiDonationReviewResponse> ReviewAsync(
            string donationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(donationId))
                throw new ArgumentException("donationId 不可為空。");

            string supabaseUrl = GetRequiredSetting("Supabase:Url");
            string serviceRoleKey = GetRequiredSetting("Supabase:ServiceRoleKey");

            using HttpClient supabaseClient = _httpClientFactory.CreateClient();

            Dictionary<string, JsonElement> donation = await GetSingleRowAsync(
                supabaseClient, supabaseUrl, serviceRoleKey, DonationTable,
                "id", donationId, "id,demand_id", "找不到捐助資料。",
                cancellationToken);

            string demandId = GetJsonStringOrNumber(
                donation, "demand_id", "捐助資料缺少 demand_id。");

            Dictionary<string, JsonElement> demand = await GetSingleRowAsync(
                supabaseClient, supabaseUrl, serviceRoleKey, DemandTable,
                "id", demandId, "id,item,conditions", "找不到物資需求。",
                cancellationToken);

            string requiredItem = GetJsonString(
                demand, "item", "物資需求沒有 item。");

            List<string> acceptedConditions = GetAcceptedConditions(demand);
            if (acceptedConditions.Count == 0)
                throw new ArgumentException("物資需求沒有可辨識的接受狀態。");

            string expectedCondition = ChooseExpectedCondition(acceptedConditions);

            List<DonationImageFile> imageFiles = await GetDonationImageFilesAsync(
                supabaseClient, supabaseUrl, serviceRoleKey, donationId,
                cancellationToken);

            List<OpenRouterImageInput> downloadedImages = new();

            foreach (DonationImageFile imageFile in imageFiles)
            {
                try
                {
                    byte[] imageBytes = await DownloadStorageFileAsync(
                        supabaseClient, supabaseUrl, serviceRoleKey,
                        imageFile.StoragePath, cancellationToken);

                    if (imageBytes.Length > 0)
                        downloadedImages.Add(new OpenRouterImageInput(
                            imageBytes, imageFile.ContentType));
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error,
                        "無法下載捐助圖片。DonationId: {DonationId}", donationId);
                }
            }

            if (downloadedImages.Count == 0)
                throw new InvalidOperationException("物資圖片無法讀取。");

            AiImageResult aiResult = await InspectImagesWithOpenRouterAsync(
                downloadedImages, requiredItem, expectedCondition,
                cancellationToken);

            bool conditionMatches = aiResult.ItemMatches &&
                acceptedConditions.Contains(aiResult.Condition);

            string aiDecision;
            string decisionText;

            if (!aiResult.ItemMatches)
            {
                aiDecision = "rejected";
                decisionText = "圖片中的物品不是需求的「" + requiredItem + "」，等待受助者最後確認";
            }
            else if (aiResult.Condition == "無法判斷")
            {
                aiDecision = "rejected";
                decisionText = "AI 暫時無法確認物資狀態，等待受助者最後確認";
            }
            else if (conditionMatches)
            {
                aiDecision = "accepted";
                decisionText = "物品種類與狀態符合接受條件，等待受助者最後確認";
            }
            else
            {
                aiDecision = "rejected";
                decisionText = "AI 初步判定為" + aiResult.Condition + "，不符合接受條件，等待受助者最後確認";
            }

            _logger.LogInformation(
                "AI 圖片分析完成。DonationId: {DonationId}, AiDecision: {AiDecision}, DetectedCondition: {DetectedCondition}",
                donationId, aiDecision, aiResult.Condition);

            await _donationRepository.UpdateAiReviewResultAsync(
                donationId,
                "pending_human_review",
                aiDecision,
                aiResult.Condition,
                aiResult.Reason,
                DateTimeOffset.UtcNow,
                cancellationToken
            );

            // 直接使用模型提供的 reason
            return new AiDonationReviewResponse
            {
                DonationId = donationId,
                RequiredItem = requiredItem,
                Status = "pending_human_review",
                AiDecision = aiDecision,
                DecisionText = decisionText,
                ItemMatches = aiResult.ItemMatches,
                DetectedCondition = aiResult.Condition,
                Reason = aiResult.Reason,
                ConditionMatches = conditionMatches,
                ImageCount = downloadedImages.Count
            };
        }

        private async Task<AiImageResult> InspectImagesWithOpenRouterAsync(
            List<OpenRouterImageInput> images,
            string requiredItem,
            string expectedCondition,
            CancellationToken cancellationToken)
        {
            string apiKey = GetRequiredSetting("OpenRouter:ApiKey");
            string model = GetRequiredSetting("OpenRouter:Model");
            string modelCondition = expectedCondition == "全新" ? "new" : "used";

            List<object> content = new()
            {
                new { type = "text", text = BuildPrompt(requiredItem, modelCondition) }
            };

            int imageIndex = 1;
            foreach (OpenRouterImageInput image in images)
            {
                string contentType = image.ContentType;
                if (string.IsNullOrWhiteSpace(contentType) ||
                    !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    contentType = "image/jpeg";

                content.Add(new
                {
                    type = "text",
                    text = "第 " + imageIndex + " 張物資照片："
                });
                content.Add(new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = "data:" + contentType + ";base64," +
                              Convert.ToBase64String(image.Bytes)
                    }
                });
                imageIndex++;
            }

            object payload = new
            {
                model,
                messages = new[] { new { role = "user", content } },
                response_format = new { type = "json_object" },
                reasoning = new { enabled = false },
                temperature = 0,
                max_tokens = 1000,
                stream = false
            };

            Exception? lastError = null;
            for (int attempt = 0; attempt <= 2; attempt++)
            {
                try
                {
                    string responseText = await SendOpenRouterRequestAsync(
                        payload, apiKey, cancellationToken);
                    return ParseOpenRouterResult(responseText);
                }
                catch (Exception error) when (
                    error is JsonException || error is InvalidOperationException)
                {
                    lastError = error;
                    if (attempt < 2)
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    else
                        throw;
                }
            }

            throw new InvalidOperationException(
                "OpenRouter 回傳內容無法使用。", lastError);
        }

        private async Task<string> SendOpenRouterRequestAsync(
            object payload,
            string apiKey,
            CancellationToken cancellationToken)
        {
            using HttpClient client = _httpClientFactory.CreateClient("OpenRouter");

            for (int attempt = 0; attempt < 3; attempt++)
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Post, "chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using HttpResponseMessage response = await client.SendAsync(
                    request, cancellationToken);
                string responseText = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "OpenRouter 呼叫失敗。StatusCode: {StatusCode}", response.StatusCode);
                    throw new HttpRequestException("OpenRouter 呼叫失敗。");
                }

                return responseText;
            }

            throw new HttpRequestException("OpenRouter 限流重試後仍失敗。");
        }

        private static AiImageResult ParseOpenRouterResult(string responseText)
        {
            using JsonDocument responseDocument = JsonDocument.Parse(responseText);
            JsonElement root = responseDocument.RootElement;

            if (!root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                throw new InvalidOperationException("OpenRouter 回傳空的 choices。");

            JsonElement firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out JsonElement message) ||
                message.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("OpenRouter 回傳的 message 格式不正確。");

            if (!message.TryGetProperty("content", out JsonElement content))
                throw new InvalidOperationException("OpenRouter 回傳內容為空。");

            string modelText = ExtractTextFromContent(content).Trim();
            if (string.IsNullOrWhiteSpace(modelText))
                throw new InvalidOperationException("OpenRouter 模型回傳空內容。");

            modelText = CleanJsonText(modelText);
            using JsonDocument resultDocument = JsonDocument.Parse(modelText);
            JsonElement result = resultDocument.RootElement;

            if (result.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("模型 JSON 結果不是物件。");

            bool itemMatches = ReadBooleanOrDefault(result, "item_matches", false);
            string condition = ReadStringOrDefault(result, "condition", "無法判斷");

            if (!IsAllowedCondition(condition) || !itemMatches)
                condition = "無法判斷";

            string reason = ReadStringOrDefault(result, "reason", "模型未提供說明");
            bool visibleDamage = ReadBooleanOrDefault(
                result, "visible_damage", condition == "有擦痕" || condition == "毀損");
            bool expired = ReadBooleanOrDefault(
                result, "expired", condition == "過期");
            bool needMoreImages = ReadBooleanOrDefault(
                result, "need_more_images", condition == "無法判斷");

            return new AiImageResult(
                itemMatches, condition, reason.Trim(), visibleDamage,
                expired, needMoreImages, true);
        }

        private static string ExtractTextFromContent(JsonElement content)
        {
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? string.Empty;

            if (content.ValueKind != JsonValueKind.Array)
                return string.Empty;

            StringBuilder builder = new();
            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    builder.Append(part.GetString());
                }
                else if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("text", out JsonElement text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
            return builder.ToString();
        }

        private static string CleanJsonText(string text)
        {
            string value = text.Trim();
            if (value.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(7);
            else if (value.StartsWith("```"))
                value = value.Substring(3);

            if (value.EndsWith("```"))
                value = value.Substring(0, value.Length - 3);

            return value.Trim();
        }

        private static bool ReadBooleanOrDefault(
            JsonElement result, string propertyName, bool defaultValue)
        {
            if (result.TryGetProperty(propertyName, out JsonElement value) &&
                (value.ValueKind == JsonValueKind.True ||
                 value.ValueKind == JsonValueKind.False))
                return value.GetBoolean();
            return defaultValue;
        }

        private static string ReadStringOrDefault(
            JsonElement result, string propertyName, string defaultValue)
        {
            if (result.TryGetProperty(propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!.Trim();
            return defaultValue;
        }

        private async Task<Dictionary<string, JsonElement>> GetSingleRowAsync(
            HttpClient client, string supabaseUrl, string serviceRoleKey,
            string tableName, string filterColumn, string filterValue,
            string selectColumns, string notFoundMessage,
            CancellationToken cancellationToken)
        {
            string url = TrimTrailingSlash(supabaseUrl) + "/rest/v1/" + tableName + "?" +
                         filterColumn + "=eq." + Uri.EscapeDataString(filterValue) +
                         "&select=" + Uri.EscapeDataString(selectColumns);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            AddSupabaseHeaders(request, serviceRoleKey);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("Supabase 查詢失敗。");

            using JsonDocument document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
                throw new KeyNotFoundException(notFoundMessage);

            return JsonObjectToDictionary(document.RootElement[0]);
        }

        private async Task<List<DonationImageFile>> GetDonationImageFilesAsync(
            HttpClient client, string supabaseUrl, string serviceRoleKey,
            string donationId, CancellationToken cancellationToken)
        {
            string url = TrimTrailingSlash(supabaseUrl) + "/rest/v1/" + FilesTable +
                         "?donation_id=eq." + Uri.EscapeDataString(donationId) +
                         "&file_type=eq.material_image&select=storage_path,mime_type";

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            AddSupabaseHeaders(request, serviceRoleKey);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("讀取捐助圖片資料失敗。");

            using JsonDocument document = JsonDocument.Parse(responseText);
            List<DonationImageFile> files = new();

            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("storage_path", out JsonElement path) ||
                    path.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(path.GetString()))
                    continue;

                string contentType = "image/jpeg";
                if (row.TryGetProperty("mime_type", out JsonElement mime) &&
                    mime.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(mime.GetString()))
                    contentType = mime.GetString()!;

                files.Add(new DonationImageFile(path.GetString()!, contentType));
            }
            return files;
        }

        private async Task<byte[]> DownloadStorageFileAsync(
            HttpClient client, string supabaseUrl, string serviceRoleKey,
            string storagePath, CancellationToken cancellationToken)
        {
            string url = TrimTrailingSlash(supabaseUrl) + "/storage/v1/object/" +
                         StorageBucket + "/" + EncodeStoragePath(storagePath);

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            AddSupabaseHeaders(request, serviceRoleKey);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("無法讀取 Storage 圖片。");

            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        private static string BuildPrompt(string requiredItem, string itemCondition)
        {
            string conditionContext = itemCondition == "new"
                ? "捐助者申報這是全新物資。"
                : "捐助者申報這是二手物資。";

            return $@"你是物資影像的初步審核助手，請使用繁體中文回覆。

受助者需求物品：
{requiredItem}

{conditionContext}

這次提供的是同一件物資的多張照片。請綜合所有照片判斷，不要只根據其中一張。

請先判斷圖片中的物品是否為「{requiredItem}」。如果不是，或圖片太模糊、太暗、角度不足，item_matches 必須是 false。

只有在物品確定是「{requiredItem}」時，才判斷狀態。
condition 只能從以下選一個：全新、二手、有擦痕、過期、毀損、無法判斷

狀態規則：
- 全新：未看到使用痕跡，包裝或物品外觀完整。
- 二手：有使用痕跡，但沒有明顯嚴重損壞，且仍可能正常使用。
- 有擦痕：有輕微刮痕、磨痕或表面使用痕跡，但沒有結構性損壞。
- 過期：只有在圖片清楚顯示有效期限，且可以確認日期已過期時才使用。
- 毀損：看到破洞、斷裂、缺件、嚴重凹陷、包裝破裂或可能無法使用的損壞。
- 無法判斷：圖片不足以確認物資狀態。

重要規則：不要因捐助者申報全新或二手就直接照抄；不要把輕微擦痕判定為毀損；看不清楚有效期限時不要判定為過期；多張照片只要一張清楚顯示重大損壞，優先判定為毀損；無法確認時選無法判斷。所有結果都需要人工審核。

請只回傳合法 JSON，不要加入 Markdown 或其他文字：
{{
  ""item_matches"": true,
  ""condition"": ""全新"",
  ""reason"": ""圖片中的物品符合需求，且外觀狀態完整"",
  ""visible_damage"": false,
  ""expired"": false,
  ""need_more_images"": false,
  ""need_manual_review"": true
}}".Trim();
        }

        private static List<string> GetAcceptedConditions(Dictionary<string, JsonElement> demand)
        {
            List<string> result = new();
            if (!demand.TryGetValue("conditions", out JsonElement conditions))
                return result;

            if (conditions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in conditions.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        AddAcceptedCondition(item.GetString(), result);
            }
            else if (conditions.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in conditions.EnumerateObject())
                    if (property.Value.ValueKind == JsonValueKind.String)
                        AddAcceptedCondition(property.Name + "：" + property.Value.GetString(), result);
            }
            return result.Distinct().ToList();
        }

        private static void AddAcceptedCondition(string? rawCondition, List<string> result)
        {
            if (string.IsNullOrWhiteSpace(rawCondition)) return;
            string[] parts = rawCondition.Replace('：', ':').Split(':', 2,
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return;

            string name = parts[0].Trim();
            string acceptance = parts[1].Trim();
            if (!AllowedConditions.Contains(name) || name == "無法判斷") return;
            if (acceptance == "接受" && !result.Contains(name)) result.Add(name);
        }

        private static string ChooseExpectedCondition(List<string> conditions)
        {
            if (conditions.Count == 0) throw new ArgumentException("沒有可接受的物資狀態。");
            if (conditions.Contains("二手") || conditions.Contains("有擦痕")) return "二手";
            if (conditions.Contains("全新")) return "全新";
            return conditions[0];
        }

        private static bool IsAllowedCondition(string condition) => AllowedConditions.Contains(condition);

        private static Dictionary<string, JsonElement> JsonObjectToDictionary(JsonElement obj) =>
            obj.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

        private static string GetJsonString(
            Dictionary<string, JsonElement> values, string propertyName, string errorMessage)
        {
            if (!values.TryGetValue(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw new ArgumentException(errorMessage);
            return value.GetString()!.Trim();
        }

        private static string GetJsonStringOrNumber(
            Dictionary<string, JsonElement> values, string propertyName, string errorMessage)
        {
            if (!values.TryGetValue(propertyName, out JsonElement value))
                throw new ArgumentException(errorMessage);
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString()!.Trim();
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
                return number.ToString(CultureInfo.InvariantCulture);
            throw new ArgumentException(errorMessage);
        }

        private string GetRequiredSetting(string settingName)
        {
            string? value = _configuration[settingName];
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("尚未設定 " + settingName + "。");
            return value;
        }

        private static void AddSupabaseHeaders(HttpRequestMessage request, string serviceRoleKey)
        {
            request.Headers.Add("apikey", serviceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
        }

        private static string EncodeStoragePath(string storagePath) => string.Join(
            "/", storagePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        private static string TrimTrailingSlash(string value) => value.TrimEnd('/');

        private sealed class DonationImageFile
        {
            public string StoragePath { get; }
            public string ContentType { get; }
            public DonationImageFile(string storagePath, string contentType)
            {
                StoragePath = storagePath;
                ContentType = contentType;
            }
        }

        private sealed class OpenRouterImageInput
        {
            public byte[] Bytes { get; }
            public string ContentType { get; }
            public OpenRouterImageInput(byte[] bytes, string contentType)
            {
                Bytes = bytes;
                ContentType = contentType;
            }
        }

        private sealed class AiImageResult
        {
            public bool ItemMatches { get; }
            public string Condition { get; }
            public string Reason { get; }
            public bool VisibleDamage { get; }
            public bool Expired { get; }
            public bool NeedMoreImages { get; }
            public bool NeedManualReview { get; }

            public AiImageResult(
                bool itemMatches, string condition, string reason,
                bool visibleDamage, bool expired, bool needMoreImages,
                bool needManualReview)
            {
                ItemMatches = itemMatches;
                Condition = condition;
                Reason = reason;
                VisibleDamage = visibleDamage;
                Expired = expired;
                NeedMoreImages = needMoreImages;
                NeedManualReview = needManualReview;
            }
        }
    }
}