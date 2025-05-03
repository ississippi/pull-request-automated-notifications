using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NotificationsService.Services
{
    public class DynamoService
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly string _tableName = "PRReviews";
        private readonly ILogger<PrController> _logger;

        public DynamoService(ILogger<PrController> logger)
        {
            _dynamoDbClient = new AmazonDynamoDBClient(); // uses default credentials and region
            _logger = logger;
        }

        public async Task<Dictionary<string, AttributeValue>> GetReviewByIdAsync(int prNumber, string repo)
        {
            // Build the DDB partition key.
            var partitionKey = $"{repo}#{prNumber}";
            _logger.LogInformation($"partitionKey: {partitionKey}");
            var request = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    { "prId", new AttributeValue { S = partitionKey } } // use the actual partition key name
                }
            };

            var response = await _dynamoDbClient.GetItemAsync(request);

            if (response.Item == null || response.Item.Count == 0)
            {
                Console.WriteLine("No item found with the specified ID.");
                return null;
            }

            return response.Item;
        }

        // Optionally: scan or query by other attributes
        public async Task<List<Dictionary<string, AttributeValue>>> GetAllReviewsAsync()
        {
            var request = new ScanRequest
            {
                TableName = _tableName,
                Limit = 10 // avoid accidental full table scan
            };

            var response = await _dynamoDbClient.ScanAsync(request);
            return response.Items;
        }
    }
}
