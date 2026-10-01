namespace Donation.Domains;

public sealed class AiDonationReviewResponse
{
    public string DonationId { get; set; } = string.Empty;

    public string RequiredItem { get; set; } = string.Empty;

    public string Status { get; set; } =
        "pending_human_review";

    public string AiDecision { get; set; } =
        "rejected";

    public string DecisionText { get; set; } =
        string.Empty;

    public bool ItemMatches { get; set; }

    public string DetectedCondition { get; set; } =
        "無法判斷";

    public string Reason { get; set; } =
        string.Empty;

    public bool ConditionMatches { get; set; }

    public int ImageCount { get; set; }
}