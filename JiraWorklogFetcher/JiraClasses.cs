namespace JiraWorklogFetcher
{
    class WorklogEntry
    {
        public string? Date { get; set; }
        public string? IssueKey { get; set; }
        public string? Author { get; set; }
        public double HoursSpent { get; set; }
        public double MinutesSpent { get; set; }
        public string? Assignee { get; set; }
    }
}
