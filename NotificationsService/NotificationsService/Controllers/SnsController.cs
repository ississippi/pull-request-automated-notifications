using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using NotificationsService.Services;
using NotificationsService.Models;


namespace NotificationsService.Controllers
{
    [ApiController]
    [Route("sns")]
    public class SnsController : ControllerBase
    {
        private readonly ILogger<SnsController> _logger;
        private readonly PrService _prService;

        public SnsController(PrService prService, ILogger<SnsController> logger)
        {
            _logger = logger;
            _prService = prService;
        }

        [HttpPost("receive")]
        public async Task<IActionResult> Receive()
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            //_logger.LogInformation("Receive: SNS message received: {0}", body);
            _logger.LogInformation("Receive: SNS message received.");

            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                var messageType = Request.Headers["x-amz-sns-message-type"].ToString();

                if (messageType == "SubscriptionConfirmation")
                {
                    var subscribeUrl = json["SubscribeURL"];
                    _logger.LogInformation("Receive: Confirming SNS subscription: {0}", subscribeUrl);
                    using var client = new HttpClient();
                    await client.GetAsync(subscribeUrl);
                    return Ok();
                }

                if (messageType == "Notification")
                {
                    var message = json["Message"];
                    _logger.LogInformation("SNS Notification: {0}", message);
                    var reviewNotification = JsonSerializer.Deserialize<ReviewNotification>(message);
                    if (reviewNotification != null)
                    {
                        try
                        {
                            _logger.LogInformation($"Pr_Number: {reviewNotification.metadata.pr_number}");
                            //_logger.LogInformation($"ReviewTitle: {reviewNotification.reviewTitle}");
                            //_logger.LogInformation($"User_Login: {reviewNotification.metadata.user_login}");
                            //_logger.LogInformation($"Created_At: {reviewNotification.metadata.created_at}");
                            //_logger.LogInformation($"Review: {reviewNotification.review}");
                            var prItem = new PrItem
                            {
                                id = reviewNotification.metadata.pr_number,
                                title = reviewNotification.reviewTitle,
                                author = reviewNotification.metadata.user_login,
                                date = reviewNotification.metadata.created_at,
                                repo = reviewNotification.metadata.repo
                                //review = "# Code Review: Lambda Function Diff\n\n## Summary\nThe diff shows the removal of a Flask-based implementation that duplicated functionality already present in the main lambda_handler function. This is generally a positive change that removes redundant code."
                                //review = reviewNotification.review
                            };
                            await _prService.BroadcastNewPrAsync(prItem);
                            _logger.LogInformation($"Review for PR #{prItem.id} sent to BroadcastNewPrAsync().");
                        }
                        catch
                        {
                            _logger.LogError("Receive: Message not properly deserialized.");
                        }


                    }
                    else
                    {
                        _logger.LogError("Receive: No message content from SNS.");
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Receive: Error processing SNS message");
            }

            return Ok();
        }
    }

}
