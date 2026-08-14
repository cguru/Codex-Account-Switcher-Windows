using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CodexAccountSwitcher.Windows
{
    internal static class AccountUsageRegistry
    {
        private const long FiveHourMinutes = 300;
        private const long WeeklyMinutes = 10080;

        public static void Normalize(IList<AccountInfo> accounts)
        {
            if (accounts == null || accounts.Count == 0) return;
            try
            {
                string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrWhiteSpace(codexHome))
                {
                    codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                }
                string path = Path.Combine(codexHome, "accounts", "registry.json");
                if (!File.Exists(path)) return;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    Normalize(accounts, ReadRegistry(stream));
                }
            }
            catch
            {
                // The human-readable codex-auth table is still a usable fallback.
            }
        }

        internal static void NormalizeFromJson(IList<AccountInfo> accounts, string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                Normalize(accounts, ReadRegistry(stream));
            }
        }

        private static UsageRegistryFile ReadRegistry(Stream stream)
        {
            var serializer = new DataContractJsonSerializer(typeof(UsageRegistryFile));
            return serializer.ReadObject(stream) as UsageRegistryFile;
        }

        private static void Normalize(IList<AccountInfo> accounts, UsageRegistryFile registry)
        {
            if (registry == null || registry.Accounts == null) return;
            foreach (AccountInfo account in accounts)
            {
                // Preserve explicit API errors such as 401/403 instead of replacing them with
                // an older registry snapshot.
                if (!account.FiveHourUsedPercent.HasValue && !account.WeeklyUsedPercent.HasValue) continue;
                UsageRegistryAccount stored = registry.Accounts.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x.Email) &&
                    string.Equals(x.Email, account.Email, StringComparison.OrdinalIgnoreCase));
                if (stored == null || stored.LastUsage == null) continue;

                UsageRegistryWindow fiveHour = FindWindow(stored.LastUsage, FiveHourMinutes);
                UsageRegistryWindow weekly = FindWindow(stored.LastUsage, WeeklyMinutes);
                ApplyWindow(account, fiveHour, true);
                ApplyWindow(account, weekly, false);
            }
        }

        private static UsageRegistryWindow FindWindow(UsageRegistrySnapshot usage, long minutes)
        {
            if (usage.Primary != null && usage.Primary.WindowMinutes == minutes) return usage.Primary;
            if (usage.Secondary != null && usage.Secondary.WindowMinutes == minutes) return usage.Secondary;
            return null;
        }

        private static void ApplyWindow(AccountInfo account, UsageRegistryWindow window, bool fiveHour)
        {
            if (window == null)
            {
                if (fiveHour)
                {
                    account.FiveHourUsage = "-";
                    account.FiveHourUsedPercent = null;
                }
                else
                {
                    account.WeeklyUsage = "-";
                    account.WeeklyUsedPercent = null;
                }
                return;
            }

            int remaining = (int)Math.Round(100.0 - window.UsedPercent, MidpointRounding.AwayFromZero);
            remaining = Math.Max(0, Math.Min(100, remaining));
            string text = FormatRemaining(remaining, window.ResetsAt);
            if (fiveHour)
            {
                account.FiveHourUsage = text;
                account.FiveHourUsedPercent = remaining;
            }
            else
            {
                account.WeeklyUsage = text;
                account.WeeklyUsedPercent = remaining;
            }
        }

        private static string FormatRemaining(int remaining, long? resetsAt)
        {
            if (!resetsAt.HasValue) return remaining + "%";
            try
            {
                DateTime local = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(resetsAt.Value).ToLocalTime();
                return L.IsKorean
                    ? string.Format("{0}% ({1:M/d HH:mm} 초기화)", remaining, local)
                    : string.Format("{0}% (resets {1:MMM d HH:mm})", remaining, local);
            }
            catch (ArgumentOutOfRangeException)
            {
                return remaining + "%";
            }
        }
    }

    [DataContract]
    internal sealed class UsageRegistryFile
    {
        [DataMember(Name = "accounts")]
        public List<UsageRegistryAccount> Accounts { get; set; }
    }

    [DataContract]
    internal sealed class UsageRegistryAccount
    {
        [DataMember(Name = "email")]
        public string Email { get; set; }

        [DataMember(Name = "last_usage")]
        public UsageRegistrySnapshot LastUsage { get; set; }
    }

    [DataContract]
    internal sealed class UsageRegistrySnapshot
    {
        [DataMember(Name = "primary")]
        public UsageRegistryWindow Primary { get; set; }

        [DataMember(Name = "secondary")]
        public UsageRegistryWindow Secondary { get; set; }
    }

    [DataContract]
    internal sealed class UsageRegistryWindow
    {
        [DataMember(Name = "used_percent")]
        public double UsedPercent { get; set; }

        [DataMember(Name = "window_minutes")]
        public long? WindowMinutes { get; set; }

        [DataMember(Name = "resets_at")]
        public long? ResetsAt { get; set; }
    }
}
