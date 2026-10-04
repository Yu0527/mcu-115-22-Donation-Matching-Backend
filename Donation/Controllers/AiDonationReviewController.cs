using Donation.Domains;
using Donation.Services;
using Microsoft.AspNetCore.Mvc;

namespace Donation.Controllers;

[ApiController]
[Route("api/ai-donation-reviews")]
public sealed class AiDonationReviewController : ControllerBase
{
    private readonly AiDonationReviewService _reviewService;

    public AiDonationReviewController(
        AiDonationReviewService reviewService
    )
    {
        _reviewService = reviewService;
    }

    [HttpPost("donations/{donationId}/ai-review")]
    [ProducesResponseType(
        typeof(AiDonationReviewResponse),
        StatusCodes.Status200OK
    )]
    public async Task<ActionResult<AiDonationReviewResponse>>
        ReviewDonationAsync(
            string donationId,
            CancellationToken cancellationToken
        )
    {
        if (string.IsNullOrWhiteSpace(donationId))
        {
            return BadRequest(
                new
                {
                    message = "donationId 不可為空。"
                }
            );
        }

        try
        {
            var result = await _reviewService.ReviewAsync(
                donationId,
                cancellationToken
            );

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(
                new
                {
                    message = "找不到捐助資料或物資需求。"
                }
            );
        }
        catch (ArgumentException error)
        {
            return BadRequest(
                new
                {
                    message = error.Message
                }
            );
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new
                {
                    message =
                        "AI 圖片分析服務暫時無法使用，請稍後再試。"
                }
            );
        }
    }
}