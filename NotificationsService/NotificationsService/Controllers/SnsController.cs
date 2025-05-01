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
            _logger.LogInformation("SNS message received: {0}", body);

            try
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                var messageType = Request.Headers["x-amz-sns-message-type"].ToString();

                if (messageType == "SubscriptionConfirmation")
                {
                    var subscribeUrl = json["SubscribeURL"];
                    _logger.LogInformation("Confirming SNS subscription: {0}", subscribeUrl);
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
                            _logger.LogInformation($"Pr_Number: {reviewNotification.Metadata.Pr_Number}");
                            _logger.LogInformation($"ReviewTitle: {reviewNotification.ReviewTitle}");
                            _logger.LogInformation($"User_Login: {reviewNotification.Metadata.User_Login}");
                            _logger.LogInformation($"Created_At: {reviewNotification.Metadata.Created_At}");
                            _logger.LogInformation($"Review: {reviewNotification.Review}");
                        }
                        catch
                        {
                            _logger.LogError("Message not properly deserialized.");
                        }

                        //var prItem = new PrItem
                        //{
                        //    id = reviewNotification.Metadata.Pr_Number,
                        //    title = reviewNotification.ReviewTitle,
                        //    author = reviewNotification.Metadata.User_Login,
                        //    date = reviewNotification.Metadata.Created_At,
                        //    review = reviewNotification.Review
                        //};
                        //await _prService.BroadcastNewPrAsync(prItem);
                    }
                    else
                    {
                        _logger.LogError("No message content from SNS.");
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SNS message");
            }

            return Ok();
        }
    }

}
