using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAccountSwitcher.Windows
{
    internal sealed class SettingsStore
    {
        private const int CurrentSettingsVersion = 2;
        private readonly string _directory;
        private readonly string _path;

        public SettingsStore()
        {
            _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexAccountSwitcherWindows");
            _path = Path.Combine(_directory, "settings.ini");
        }

        public AppSettings Load()
        {
            AppSettings settings = AppSettings.Defaults();
            if (!File.Exists(_path)) return settings;
            int settingsVersion = 0;
            foreach (string raw in File.ReadAllLines(_path, Encoding.UTF8))
            {
                string[] parts = raw.Split(new[] { '=' }, 2);
                if (parts.Length != 2) continue;
                string key = parts[0].Trim().ToLowerInvariant();
                int version;
                if (key == "settings_version" && int.TryParse(parts[1].Trim(), out version))
                {
                    settingsVersion = version;
                    continue;
                }
                bool value;
                if (!bool.TryParse(parts[1].Trim(), out value)) continue;
                if (key == "use_api_usage") settings.UseApiUsage = value;
                if (key == "start_with_windows") settings.StartWithWindows = value;
                if (key == "confirm_before_switch") settings.ConfirmBeforeSwitch = value;
            }
            // v1 wrote the old local-only default to disk even when the user never chose it.
            // Migrate that implicit default once; explicit choices made by v2+ remain intact.
            if (settingsVersion < CurrentSettingsVersion) settings.UseApiUsage = true;
            return settings;
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(_directory);
            string temp = _path + ".tmp";
            File.WriteAllLines(temp, new[]
            {
                "settings_version=" + CurrentSettingsVersion,
                "use_api_usage=" + settings.UseApiUsage,
                "start_with_windows=" + settings.StartWithWindows,
                "confirm_before_switch=" + settings.ConfirmBeforeSwitch
            }, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
    }

    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CodexAccountSwitcherWindows";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                {
                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    key.SetValue(ValueName, "\"" + exe + "\" --tray", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    internal sealed class CodexAuthService
    {
        private readonly bool _demoMode;
        private readonly string _commandPath;

        public CodexAuthService(bool demoMode)
        {
            _demoMode = demoMode;
            _commandPath = demoMode ? "demo" : LocateCodexAuth();
        }

        public bool IsAvailable
        {
            get { return _demoMode || !string.IsNullOrEmpty(_commandPath); }
        }

        public bool IsDemoMode
        {
            get { return _demoMode; }
        }

        public string CommandPath
        {
            get { return _commandPath; }
        }

        public async Task<IList<AccountInfo>> ListAccountsAsync(bool useApi)
        {
            if (_demoMode)
            {
                await Task.Delay(150);
                return DemoAccounts();
            }
            EnsureAvailable();
            CommandResult result = await RunAsync(new[] { "list", useApi ? "--api" : "--skip-api" }, 45000);
            IList<AccountInfo> accounts = AccountTableParser.Parse(result.Output, useApi);
            AccountUsageRegistry.Normalize(accounts);
            // codex-auth may print a valid table and then return a non-zero exit code when an
            // optional post-refresh step is unavailable. The verified table remains usable.
            if (accounts.Count > 0) return accounts;
            if (!result.Success) throw new InvalidOperationException(CleanError(result.Output,
                L.T("계정 목록을 읽지 못했습니다.", "Could not read the account list.")));
            return accounts;
        }

        public async Task<CommandResult> SwitchAsync(string accountKey)
        {
            if (_demoMode)
            {
                await Task.Delay(400);
                return new CommandResult { ExitCode = 0, Output = "demo switch" };
            }
            EnsureAvailable();
            return await RunAsync(new[] { "switch", accountKey }, 30000);
        }

        public Process StartLogin(bool deviceCode, string bundledCliDirectory)
        {
            EnsureAvailable();
            var args = new List<string> { "login" };
            if (deviceCode) args.Add("--device-auth");
            string command = BuildCmdInvocation(_commandPath, args.ToArray(), true);
            var info = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /s /c " + command,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };
            PrependPath(info, AppDomain.CurrentDomain.BaseDirectory);
            if (!string.IsNullOrWhiteSpace(bundledCliDirectory))
            {
                PrependPath(info, bundledCliDirectory);
            }
            return Process.Start(info);
        }

        public async Task<CommandResult> VersionAsync()
        {
            if (_demoMode) return new CommandResult { ExitCode = 0, Output = "codex-auth demo" };
            EnsureAvailable();
            return await RunAsync(new[] { "--version" }, 10000);
        }

        private async Task<CommandResult> RunAsync(string[] args, int timeoutMs)
        {
            string command = BuildCmdInvocation(_commandPath, args, false);
            var info = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /s /c " + command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            info.EnvironmentVariables["NO_COLOR"] = "1";
            info.EnvironmentVariables["TERM"] = "dumb";
            PrependPath(info, AppDomain.CurrentDomain.BaseDirectory);

            using (Process process = new Process { StartInfo = info })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(timeoutMs));
                if (!exited)
                {
                    try { process.Kill(); }
                    catch { }
                }
                string output = (await stdout) + (await stderr);
                return new CommandResult
                {
                    ExitCode = exited ? process.ExitCode : 124,
                    Output = output.Trim(),
                    TimedOut = !exited
                };
            }
        }

        private static string BuildCmdInvocation(string path, string[] args, bool keepReadable)
        {
            var pieces = new List<string> { Quote(path) };
            pieces.AddRange(args.Select(Quote));
            string command = string.Join(" ", pieces.ToArray());
            if (keepReadable) command += L.T(
                " & echo. & echo 완료되었습니다. 이 창을 닫아도 됩니다. & pause",
                " & echo. & echo Finished. You may close this window. & pause");
            return "\"" + command + "\"";
        }

        private static void PrependPath(ProcessStartInfo info, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            string currentPath = info.EnvironmentVariables["PATH"] ?? string.Empty;
            info.EnvironmentVariables["PATH"] = directory.TrimEnd(Path.DirectorySeparatorChar) + ";" + currentPath;
        }

        private static string Quote(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string LocateCodexAuth()
        {
            var candidates = new List<string>();
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-auth.exe"));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-auth.cmd"));
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            candidates.Add(Path.Combine(appData, "npm", "codex-auth.cmd"));
            candidates.Add(Path.Combine(appData, "npm", "codex-auth.exe"));

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                string expanded = Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"'));
                candidates.Add(Path.Combine(expanded, "codex-auth.cmd"));
                candidates.Add(Path.Combine(expanded, "codex-auth.exe"));
            }

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string CleanError(string output, string fallback)
        {
            if (string.IsNullOrWhiteSpace(output)) return fallback;
            string value = output.Trim();
            return value.Length > 1200 ? value.Substring(0, 1200) + "…" : value;
        }

        private void EnsureAvailable()
        {
            if (!IsAvailable)
            {
                throw new FileNotFoundException(
                    L.T("codex-auth를 찾지 못했습니다. 먼저 npm install -g @loongphy/codex-auth 를 실행해 주세요.",
                        "codex-auth was not found. Run npm install -g @loongphy/codex-auth first."));
            }
        }

        private static IList<AccountInfo> DemoAccounts()
        {
            return new List<AccountInfo>
            {
                new AccountInfo { Selector="01", Email="work@example.com", Plan="Pro", FiveHourUsage="31% (16:40)", WeeklyUsage=L.T("62% (금 09:00)", "62% (Fri 09:00)"), FiveHourUsedPercent=31, WeeklyUsedPercent=62, LastActivity=L.T("방금", "just now"), IsActive=true },
                new AccountInfo { Selector="02", Email="personal@example.com", Plan="Plus", FiveHourUsage="8% (18:15)", WeeklyUsage=L.T("24% (월 11:30)", "24% (Mon 11:30)"), FiveHourUsedPercent=8, WeeklyUsedPercent=24, LastActivity=L.T("1시간 전", "1 hour ago"), IsActive=false },
                new AccountInfo { Selector="03", Email="backup@example.com", Plan="Plus", FiveHourUsage="-", WeeklyUsage="-", LastActivity=L.T("2일 전", "2 days ago"), IsActive=false }
            };
        }
    }

    internal static class CodexAppManager
    {
        private const string CodexAppId = @"OpenAI.Codex_2p2nqsd0c76g0!App";

        public static string FindExternalCliProcessDescription()
        {
            var ids = new List<int>();
            foreach (Process process in Process.GetProcessesByName("codex"))
            {
                string path = TryGetPath(process);
                if (string.IsNullOrEmpty(path) || path.IndexOf(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) < 0)
                    ids.Add(process.Id);
            }
            return ids.Count == 0 ? null : string.Join(", ", ids.Select(x => x.ToString()).ToArray());
        }

        public static void StopCodexDesktop()
        {
            List<Process> processes = FindPackagedProcesses();
            foreach (Process process in processes.Where(p => p.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)))
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow(); }
                catch { }
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
                processes = FindPackagedProcesses();
                if (processes.Count == 0) return;
            }

            foreach (Process process in processes)
            {
                try { process.Kill(); }
                catch { }
            }
            Thread.Sleep(800);
            if (FindPackagedProcesses().Count > 0)
                throw new InvalidOperationException(L.T("Codex Windows 앱 프로세스를 모두 종료하지 못했습니다.",
                    "Could not stop all Codex Windows app processes."));
        }

        public static void LaunchCodexDesktop()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:AppsFolder\\" + CodexAppId,
                UseShellExecute = true
            });
        }

        public static string FindBundledCliDirectory()
        {
            foreach (Process process in Process.GetProcessesByName("ChatGPT"))
            {
                string appPath = TryGetPath(process);
                if (string.IsNullOrEmpty(appPath) ||
                    appPath.IndexOf(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string candidate = Path.Combine(Path.GetDirectoryName(appPath), "resources", "codex.exe");
                if (File.Exists(candidate)) return Path.GetDirectoryName(candidate);
            }
            return null;
        }

        private static List<Process> FindPackagedProcesses()
        {
            var result = new List<Process>();
            foreach (string name in new[] { "ChatGPT", "codex", "codex-code-mode-host" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    string path = TryGetPath(process);
                    if (!string.IsNullOrEmpty(path) && path.IndexOf(@"\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0)
                        result.Add(process);
                }
            }
            return result;
        }

        private static string TryGetPath(Process process)
        {
            try { return process.MainModule.FileName; }
            catch { return null; }
        }
    }
}
