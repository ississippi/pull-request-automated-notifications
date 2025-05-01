namespace NotificationsService.Models
{
    public class PrItem
    {
        public string id { get; set; }
        public string title { get; set; }
        public string author { get; set; }
        public string date { get; set; }
        public string review { get; set; }
    }

    public class ReviewNotification
    {
        public string ReviewTitle { get; set; }
        public Metadata Metadata { get; set; }
        public string Review { get; set; }
    }

    public class Metadata
    {
        public string Pr_Number { get; set; }
        public string Repo { get; set; }
        public string Pr_Title { get; set; }
        public string User_Login { get; set; }
        public string Created_At { get; set; }
        public string Pr_State { get; set; }
        public string Pr_Body { get; set; }
        public string Html_Url { get; set; }
        public string Head_Sha { get; set; }
        public string Base_Ref { get; set; }
    }

}
