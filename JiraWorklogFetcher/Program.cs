using Figgle;
using JiraWorklogFetcher;

Colorful.Console.WriteLine(FiggleFonts.Slant.Render("Jira Worklog Fetcher !"));

await JiraWorklog.FetchWorklogs();
