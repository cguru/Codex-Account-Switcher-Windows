using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;

namespace CodexAccountSwitcher.Windows
{
    internal static class Program
    {
        private const string MutexName = @"Local\CodexAccountSwitcherWindows-7D9A6A7B";

        [STAThread]
        private static int Main(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--lang", StringComparison.OrdinalIgnoreCase) &&
                    (args[i + 1].Equals("ko", StringComparison.OrdinalIgnoreCase) ||
                     args[i + 1].Equals("en", StringComparison.OrdinalIgnoreCase)))
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo(args[i + 1]);
                    break;
                }
            }
            bool selfTest = args.Any(x => x.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
            if (selfTest) return SelfTest.Run();

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    System.Windows.Forms.MessageBox.Show(L.T(
                        "Codex Account Switcher가 이미 실행 중입니다. 작업 표시줄의 트레이 아이콘을 확인해 주세요.",
                        "Codex Account Switcher is already running. Check its system tray icon."),
                        "Codex Account Switcher", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    return 0;
                }

                bool demo = args.Any(x => x.Equals("--demo", StringComparison.OrdinalIgnoreCase));
                bool tray = args.Any(x => x.Equals("--tray", StringComparison.OrdinalIgnoreCase));
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var window = new MainWindow(new CodexAuthService(demo), tray);
                app.Run(window);
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }

    internal static class SelfTest
    {
        public static int Run()
        {
            try
            {
                string sample = "     ACCOUNT               PLAN  5H USAGE       WEEKLY USAGE     LAST ACTIVITY\n" +
                    "--------------------------------------------------------------------------------\n" +
                    "* 01 work@example.com      Pro   31% (16:40)    62% (Fri 09:00)  just now\n" +
                    "  02 personal@example.com  Plus  -             401               1 hour ago\n";
                var parsed = AccountTableParser.Parse(sample, true);
                Require(parsed.Count == 2, "account count");
                Require(parsed[0].IsActive && parsed[0].Selector == "01", "active selector");
                Require(parsed[0].SwitchKey == "work@example.com", "email switch key");
                Require(parsed[0].FiveHourUsedPercent == 31 && parsed[0].WeeklyUsedPercent == 62, "usage percent");
                Require(parsed[1].WeeklyUsage == L.T("로그인 만료", "Login expired"), "error mapping");
                string nodeSample = "* 01 work@example.com Pro NodeJsRequired NodeJsRequired -";
                var nodeParsed = AccountTableParser.Parse(nodeSample, true);
                Require(nodeParsed.Count == 1 && nodeParsed[0].FiveHourUsage ==
                    L.T("설치 구성요소 누락", "Installation component missing"),
                    "node runtime mapping");
                Console.WriteLine("SELF-TEST PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF-TEST FAILED: " + ex.Message);
                return 1;
            }
        }

        private static void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
        }
    }
}
