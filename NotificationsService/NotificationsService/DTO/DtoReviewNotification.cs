namespace NotificationsService.Models
{
    public class SnsReviewDto
    {
        public SnsReviewDto()
        {

        }

        public PrItem GetPrItem(ReviewNotification r)
        {
            return new PrItem
            {
                id = r.metadata.pr_number,
                title = r.reviewTitle,
                author = r.metadata.user_login,
                date = r.metadata.created_at,
                repo = r.metadata.repo,
                review = r.review
            };
        }
    }
    public class PrItem
    {
        public string id { get; set; }
        public string title { get; set; }
        public string author { get; set; }
        public string date { get; set; }
        public string repo { get; set; }
        public string review { get; set; }
    }

    public class ReviewNotification
    {
        public string reviewTitle { get; set; }
        public Metadata metadata { get; set; }
        public string review { get; set; }
    }

    public class Metadata
    {
        public string pr_number { get; set; }
        public string repo { get; set; }
        public string pr_title { get; set; }
        public string user_login { get; set; }
        public string created_at { get; set; }
        public string pr_state { get; set; }
        public string pr_body { get; set; }
        public string html_url { get; set; }
        public string head_sha { get; set; }
        public string base_ref { get; set; }
    }

}
