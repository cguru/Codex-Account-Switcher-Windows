using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodexAccountSwitcher.Windows
{
    internal sealed class AccountInfo
    {
        public string Selector { get; set; }
        public string Email { get; set; }
        public string Plan { get; set; }
        public string FiveHourUsage { get; set; }
        public string WeeklyUsage { get; set; }
        public int? FiveHourUsedPercent { get; set; }
        public int? WeeklyUsedPercent { get; set; }
        public string LastActivity { get; set; }
        public bool IsActive { get; set; }

        public string DisplayName
        {
            get { return string.IsNullOrWhiteSpace(Email) ? L.T("계정 ", "Account ") + Selector : Email; }
        }

        public string SwitchKey
        {
            get { return Email; }
        }
    }

    internal sealed class CommandResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public bool TimedOut { get; set; }

        public bool Success
        {
            get { return ExitCode == 0 && !TimedOut; }
        }
    }

    internal sealed class SwitchResult
    {
        public bool Success { get; set; }
        public bool RolledBack { get; set; }
        public string Message { get; set; }
        public IList<AccountInfo> Accounts { get; set; }
    }

    internal sealed class AppSettings
    {
        public bool UseApiUsage { get; set; }
        public bool StartWithWindows { get; set; }
        public bool ConfirmBeforeSwitch { get; set; }

        public static AppSettings Defaults()
        {
            return new AppSettings
            {
                UseApiUsage = false,
                StartWithWindows = false,
                ConfirmBeforeSwitch = true
            };
        }
    }

    internal static class AccountTableParser
    {
        private static readonly Regex Ansi = new Regex("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

        public static IList<AccountInfo> Parse(string output, bool usageIsLive)
        {
            var accounts = new List<AccountInfo>();
            if (string.IsNullOrWhiteSpace(output)) return accounts;

            string[] lines = output.Replace("\r", string.Empty).Split('\n');
            foreach (string raw in lines)
            {
                string line = Ansi.Replace(raw ?? string.Empty, string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("ACCOUNT", StringComparison.OrdinalIgnoreCase) ||
                    line.All(ch => ch == '-' || char.IsWhiteSpace(ch)))
                {
                    continue;
                }

                string[] tokens = Regex.Split(line, "\\s+").Where(x => x.Length > 0).ToArray();
                if (tokens.Length == 0) continue;

                bool active = tokens[0] == "*";
                int offset = active ? 1 : 0;
                if (tokens.Length < offset + 3) continue;
                if (!tokens[offset].All(char.IsDigit)) continue;

                int cursor = offset + 3;
                UsageField five = ParseUsage(tokens, cursor, usageIsLive);
                cursor = five.NextIndex;
                UsageField weekly = ParseUsage(tokens, cursor, usageIsLive);
                cursor = weekly.NextIndex;

                accounts.Add(new AccountInfo
                {
                    Selector = tokens[offset],
                    Email = tokens[offset + 1],
                    Plan = tokens[offset + 2],
                    FiveHourUsage = five.Text,
                    WeeklyUsage = weekly.Text,
                    FiveHourUsedPercent = five.Percent,
                    WeeklyUsedPercent = weekly.Percent,
                    LastActivity = cursor < tokens.Length ? string.Join(" ", tokens.Skip(cursor).ToArray()) : "-",
                    IsActive = active
                });
            }
            return accounts;
        }

        private static UsageField ParseUsage(string[] tokens, int start, bool usageIsLive)
        {
            if (start >= tokens.Length) return new UsageField("-", null, start);
            string first = tokens[start];
            if (first == "-") return new UsageField("-", null, start + 1);

            if (!first.Contains("%"))
            {
                string error = first.Equals("NodeJsRequired", StringComparison.OrdinalIgnoreCase)
                    ? L.T("설치 구성요소 누락", "Installation component missing") :
                    first == "400" || first == "401" ? L.T("로그인 만료", "Login expired") :
                    first == "403" ? L.T("조회 차단", "Access blocked") : usageIsLive ? L.T("조회 불가", "Unavailable") : "-";
                return new UsageField(error, null, start + 1);
            }

            var parts = new List<string> { first };
            int cursor = start + 1;
            if (cursor < tokens.Length && tokens[cursor].StartsWith("(", StringComparison.Ordinal))
            {
                while (cursor < tokens.Length)
                {
                    parts.Add(tokens[cursor]);
                    bool done = tokens[cursor].EndsWith(")", StringComparison.Ordinal);
                    cursor++;
                    if (done) break;
                }
            }

            Match match = Regex.Match(first, "^(?<percent>\\d+)%");
            int parsed;
            int? percent = match.Success && int.TryParse(match.Groups["percent"].Value, out parsed)
                ? (int?)Math.Max(0, Math.Min(100, parsed))
                : null;
            return new UsageField(usageIsLive ? string.Join(" ", parts.ToArray()) : first, percent, cursor);
        }

        private sealed class UsageField
        {
            public UsageField(string text, int? percent, int nextIndex)
            {
                Text = text;
                Percent = percent;
                NextIndex = nextIndex;
            }

            public string Text { get; private set; }
            public int? Percent { get; private set; }
            public int NextIndex { get; private set; }
        }
    }
}
