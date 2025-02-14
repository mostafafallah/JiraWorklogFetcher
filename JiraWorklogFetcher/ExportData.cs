using System.Globalization;
using System.Text;

namespace JiraWorklogFetcher
{
    internal class ExportData
    {
        public static string SaveToCsv(List<WorklogEntry> worklogEntries, bool convertToPersianDate = false)
        {
            try
            {
                var csvContent = new StringBuilder();
                csvContent.AppendLine("Register Date,Issue Key,Issue Title,Creator,Hours,Minutes,Assigne To,Comment");

                foreach (var entry in worklogEntries)
                {
                    var date = entry.Date;
                    if (convertToPersianDate && DateTime.TryParse(date, out var dateObj))
                    {
                        var persianCalendar = new PersianCalendar();
                        date = $"{persianCalendar.GetYear(dateObj)}/{persianCalendar.GetMonth(dateObj):D2}/{persianCalendar.GetDayOfMonth(dateObj):D2}";
                    }

                    csvContent.AppendLine($"{date},{entry.IssueKey},{entry.IssueSummary},{entry.Author},{entry.HoursSpent},{entry.MinutesSpent},{entry.Assignee},{entry.Comment}");
                }

                string filePath = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
                File.WriteAllText(filePath, csvContent.ToString(), Encoding.UTF8);

                return filePath;
            }
            catch (IOException ex)
            {
                throw new IOException("Error in saving file: " + ex.Message, ex);
            }
        }
    }
}