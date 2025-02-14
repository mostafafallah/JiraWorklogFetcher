using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JiraWorklogFetcher
{
    internal class JiraWorklog
    {
        private static string? JIRA_DOMAIN;
        private static string? username;
        private static string? apiToken;
        private static string? jqlQuery;

        // Configuration object to load appsettings.json
        private static IConfiguration? Configuration;

        static void JiraWorklogConfig()
        {
            // Build configuration from appsettings.json
            Configuration = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
                //.AddEnvironmentVariables();
                .Build();

            // Load the Jira settings from appsettings.json
            JIRA_DOMAIN = Configuration["Jira:Domain"];
            username = Configuration["Jira:Username"];
            apiToken = Configuration["Jira:ApiToken"];
            jqlQuery = Configuration["Jira:JqlQuery"];
        }


        internal static async Task FetchWorklogs()
        {
            JiraWorklogConfig();

            using (HttpClient client = new HttpClient())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                client.BaseAddress = new Uri(JIRA_DOMAIN);
                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{apiToken}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                // Finding items from Jira server
                Colorful.Console.WriteLine($"{Environment.NewLine}Finding items from Jira server...{Environment.NewLine}");
                List<string> issueKeys = await GetIssuesByJQL(client, jqlQuery).ConfigureAwait(false);

                if (!issueKeys.Any())
                {
                    Colorful.Console.WriteLine($"{Environment.NewLine}Nothing Found! Check JQL Or Configs of you JIRA!", Color.Red);
                    return;
                }

                Colorful.Console.WriteLine($"{issueKeys.Count} items found!", Color.LightGreen);

                // Gathering worklogs
                int counter = 1;
                List<WorklogEntry> worklogEntries = new List<WorklogEntry>();

                foreach (var issueKey in issueKeys)
                {
                    await GetWorklogsForIssue(client, issueKey, worklogEntries);
                    ShowProgressBar(counter++, issueKeys.Count, issueKey); // Showing progress bar
                }

                stopwatch.Stop();
                Colorful.Console.WriteLine($"{Environment.NewLine}Worklogs gathering finished in {stopwatch.ElapsedMilliseconds / 1000.0} seconds.");

                try
                {
                    Console.WriteLine($"Finished successfully! Result is in: {ExportData.SaveToCsv(worklogEntries, true)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        /// Displays a progress bar in the console.
        /// </summary>
        /// <param name="progress">The current progress.</param>
        /// <param name="total">The total amount of work to be done.</param>
        /// <param name="text">Text to display alongside the progress bar.</param>
        static void ShowProgressBar(int progress, int total, string text)
        {
            double percent = (progress / (double)total) * 100;
            int barLength = 50; // Length of progress bar (based on your idea)
            int filledLength = (int)(barLength * progress / total);

            Console.Write("\r["); // Start progress bar
            Console.Write(new string('█', filledLength)); // Filled portion of the progress
            Console.Write(new string('-', barLength - filledLength)); // Empty portion of the progress
            Console.Write($"] {percent:0.0}% - {text}"); // Displaying percentage
        }

        /// <summary>
        /// Finds issues in Jira based on a JQL query.
        /// </summary>
        /// <param name="client">The HttpClient instance used for making requests.</param>
        /// <param name="jql">The JQL query to use for finding issues.</param>
        /// <returns>A list of issue keys matching the JQL query.</returns>
        static async Task<List<string>> GetIssuesByJQL(HttpClient client, string jql)
        {
            List<string> issueKeys = new List<string>();
            try
            {
                string requestBody = JsonSerializer.Serialize(new { jql = jql, fields = new[] { "key" }, maxResults = 200 });
                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.PostAsync("/rest/api/2/search", content).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using JsonDocument doc = JsonDocument.Parse(result);
                    issueKeys.AddRange(doc.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("key").GetString()));
                }
                else
                {
                    Console.WriteLine($"Error in finding issues: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return issueKeys;
        }

        /// <summary>
        /// Finds worklogs for a specific issue.
        /// </summary>
        /// <param name="client">The HttpClient instance used for making requests.</param>
        /// <param name="issueKey">The issue key to fetch worklogs for.</param>
        /// <param name="worklogEntries">The list of worklog entries to add to.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        static async Task GetWorklogsForIssue(HttpClient client, string issueKey, List<WorklogEntry> worklogEntries)
        {
            try
            {
                // API request for finding assignee of the issue
                var issueResponse = await client.GetAsync($"/rest/api/2/issue/{issueKey}?fields=assignee").ConfigureAwait(false);

                // API request for fetching worklogs of the issue
                var worklogResponse = await client.GetAsync($"/rest/api/2/issue/{issueKey}/worklog").ConfigureAwait(false);

                if (!issueResponse.IsSuccessStatusCode || !worklogResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error in finding worklogs of issue {issueKey}");
                    return;
                }

                // Finding assignee field
                string issueResult = await issueResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                string? assignee = "Unassigned"; // Default value - you can change it
                using (JsonDocument doc = JsonDocument.Parse(issueResult))
                {
                    if (doc.RootElement.GetProperty("fields").TryGetProperty("assignee", out JsonElement assigneeElement) && assigneeElement.ValueKind != JsonValueKind.Null)
                    {
                        assignee = assigneeElement.GetProperty("displayName").GetString();
                    }
                }

                // Working on worklogs
                string worklogResult = await worklogResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument doc = JsonDocument.Parse(worklogResult))
                {
                    foreach (var worklog in doc.RootElement.GetProperty("worklogs").EnumerateArray())
                    {
                        var author = worklog.GetProperty("author").GetProperty("displayName").GetString();
                        var timeSpentSeconds = worklog.GetProperty("timeSpentSeconds").GetInt32();
                        var created = worklog.GetProperty("started").GetString();
                        DateTime date = DateTime.ParseExact(created, "yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

                        // Adding worklog entry to the list
                        worklogEntries.Add(new WorklogEntry
                        {
                            Date = date.ToString("yyyy-MM-dd"),
                            IssueKey = issueKey,
                            Author = author,
                            HoursSpent = Math.Round(timeSpentSeconds / 3600.0, 2),
                            MinutesSpent = Math.Round(timeSpentSeconds / 60.0, 0),
                            Assignee = assignee
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing worklogs for issue {issueKey}: {ex.Message}");
            }
        }
    }
}
