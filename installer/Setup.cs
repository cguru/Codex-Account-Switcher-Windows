using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Account Switcher Setup")]
[assembly: AssemblyDescription("Installer for Codex Account Switcher")]
[assembly: AssemblyCompany("cguru")]
[assembly: AssemblyProduct("Codex Account Switcher Setup")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace CodexAccountSwitcher.Setup
{
    internal static class Program
    {
        private const string ProductName = "Codex Account Switcher";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CodexAccountSwitcherWindows";
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "CodexAccountSwitcherWindows";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                    return BeginUninstall();
                if (args.Length > 0 && args[0].Equals("--finish-uninstall", StringComparison.OrdinalIgnoreCase))
                    return FinishUninstall(args);
                bool silent = args.Length > 0 && (args[0].Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                    args[0].Equals("/S", StringComparison.OrdinalIgnoreCase));
                return Install(silent);
            }
            catch (Exception ex)
            {
                MessageBox.Show(T("설치를 완료하지 못했습니다.\n\n", "Installation could not be completed.\n\n") + ex.Message, ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Install(bool silent)
        {
            string installDirectory = ExpectedInstallDirectory();
            string appPath = Path.Combine(installDirectory, "CodexAccountSwitcher.exe");
            string authPath = Path.Combine(installDirectory, "codex-auth.exe");
            string licensePath = Path.Combine(installDirectory, "LICENSE-codex-auth.txt");
            string nodePath = Path.Combine(installDirectory, "node.exe");
            string nodeLicensePath = Path.Combine(installDirectory, "LICENSE-node.txt");
            string uninstallPath = Path.Combine(installDirectory, "Uninstall Codex Account Switcher.exe");

            StopSwitcher();
            Directory.CreateDirectory(installDirectory);
            WriteResource("SwitcherPayload", appPath);
            WriteResource("CodexAuthPayload", authPath);
            WriteResource("CodexAuthLicense", licensePath);
            WriteZipEntryResource("NodePayloadZip", "/node.exe", nodePath);
            WriteResource("NodeLicense", nodeLicensePath);
            File.Copy(CurrentExecutable(), uninstallPath, true);

            CreateShortcuts(appPath, installDirectory);
            RegisterUninstaller(appPath, uninstallPath, installDirectory);

            Process.Start(new ProcessStartInfo { FileName = appPath, UseShellExecute = true });
            if (!silent)
            {
                MessageBox.Show(T(
                    "설치가 완료되었습니다.\n\n바탕 화면과 시작 메뉴에서 실행할 수 있고, 창을 닫으면 트레이에서 계속 실행됩니다.",
                    "Installation is complete.\n\nLaunch it from the desktop or Start menu. Closing the window keeps it running in the system tray."),
                    ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        private static int BeginUninstall()
        {
            DialogResult answer = MessageBox.Show(
                T("Codex Account Switcher를 제거할까요?\n\nCodex 계정과 세션은 삭제하지 않습니다.",
                    "Uninstall Codex Account Switcher?\n\nYour Codex accounts and sessions will not be deleted."),
                ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return 0;

            string tempCopy = Path.Combine(Path.GetTempPath(), "CodexAccountSwitcher-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(CurrentExecutable(), tempCopy, true);
            Process.Start(new ProcessStartInfo
            {
                FileName = tempCopy,
                Arguments = "--finish-uninstall \"" + ExpectedInstallDirectory() + "\" " + Process.GetCurrentProcess().Id,
                UseShellExecute = true
            });
            return 0;
        }

        private static int FinishUninstall(string[] args)
        {
            if (args.Length < 3) throw new InvalidOperationException(T(
                "제거 인수가 올바르지 않습니다.", "The uninstall arguments are invalid."));
            string expected = Path.GetFullPath(ExpectedInstallDirectory()).TrimEnd(Path.DirectorySeparatorChar);
            string requested = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar);
            if (!requested.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("예상하지 못한 제거 경로입니다.",
                    "The uninstall path is not the expected installation path."));

            int parentId;
            if (int.TryParse(args[2], out parentId))
            {
                try { Process.GetProcessById(parentId).WaitForExit(5000); }
                catch { }
            }

            StopSwitcher();
            RemoveShortcuts();
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(StartupKey, true))
            {
                if (run != null) run.DeleteValue(StartupValue, false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            foreach (string file in new[]
            {
                "CodexAccountSwitcher.exe", "codex-auth.exe", "LICENSE-codex-auth.txt", "node.exe", "LICENSE-node.txt",
                "Uninstall Codex Account Switcher.exe"
            })
            {
                string target = Path.Combine(requested, file);
                if (File.Exists(target)) File.Delete(target);
            }
            if (Directory.Exists(requested) && Directory.GetFileSystemEntries(requested).Length == 0)
                Directory.Delete(requested, false);

            MessageBox.Show(T("제거가 완료되었습니다. Codex 계정과 세션은 그대로 유지했습니다.",
                    "Uninstall is complete. Your Codex accounts and sessions were preserved."),
                ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static void WriteResource(string resourceName, string destination)
        {
            string temp = destination + ".new";
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException(resourceName +
                    T(" 설치 데이터가 없습니다.", " installation data is missing."));
                using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
            }
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }

        private static void WriteZipEntryResource(string resourceName, string entrySuffix, string destination)
        {
            string temp = destination + ".new";
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException(resourceName +
                    T(" 설치 데이터가 없습니다.", " installation data is missing."));
                using (ZipArchive archive = new ZipArchive(input, ZipArchiveMode.Read, false))
                {
                    ZipArchiveEntry selected = null;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(entrySuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            selected = entry;
                            break;
                        }
                    }
                    if (selected == null) throw new InvalidOperationException(entrySuffix +
                        T(" 런타임 파일이 없습니다.", " runtime file is missing."));
                    using (Stream source = selected.Open())
                    using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                        source.CopyTo(output);
                }
            }
            if (File.Exists(destination)) File.Replace(temp, destination, null);
            else File.Move(temp, destination);
        }

        private static void CreateShortcuts(string appPath, string installDirectory)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            CreateShortcut(Path.Combine(desktop, ProductName + ".lnk"), appPath, installDirectory);
            CreateShortcut(Path.Combine(programs, ProductName + ".lnk"), appPath, installDirectory);
            CreateShortcut(Path.Combine(programs, T("Codex Account Switcher 제거.lnk", "Uninstall Codex Account Switcher.lnk")),
                Path.Combine(installDirectory, "Uninstall Codex Account Switcher.exe"), installDirectory);
        }

        private static void CreateShortcut(string shortcutPath, string target, string workingDirectory)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic dynamicShell = shell;
                shortcut = dynamicShell.CreateShortcut(shortcutPath);
                dynamic dynamicShortcut = shortcut;
                dynamicShortcut.TargetPath = target;
                dynamicShortcut.WorkingDirectory = workingDirectory;
                dynamicShortcut.IconLocation = target + ",0";
                dynamicShortcut.Description = T("Codex 계정을 안전하게 전환합니다",
                    "Safely switch between Codex accounts");
                dynamicShortcut.Save();
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RemoveShortcuts()
        {
            foreach (string path in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ProductName + ".lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Codex Account Switcher 제거.lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Uninstall Codex Account Switcher.lnk")
            })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void RegisterUninstaller(string appPath, string uninstallPath, string installDirectory)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", ProductName);
                key.SetValue("DisplayVersion", "1.1.2");
                key.SetValue("Publisher", "cguru");
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("DisplayIcon", appPath);
                key.SetValue("UninstallString", "\"" + uninstallPath + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        private static void StopSwitcher()
        {
            foreach (Process process in Process.GetProcessesByName("CodexAccountSwitcher"))
            {
                try { process.Kill(); process.WaitForExit(3000); }
                catch { }
            }
            Thread.Sleep(200);
        }

        private static string ExpectedInstallDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", ProductName);
        }

        private static string CurrentExecutable()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        private static string T(string korean, string english)
        {
            return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ko",
                StringComparison.OrdinalIgnoreCase) ? korean : english;
        }
    }
}
