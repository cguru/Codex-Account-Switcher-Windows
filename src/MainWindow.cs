using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace CodexAccountSwitcher.Windows
{
    internal sealed class MainWindow : Window
    {
        private static readonly SolidColorBrush PageBrush = Brush("#F5F7FB");
        private static readonly SolidColorBrush CardBrush = Brush("#FFFFFF");
        private static readonly SolidColorBrush TextBrush = Brush("#182230");
        private static readonly SolidColorBrush MutedBrush = Brush("#667085");
        private static readonly SolidColorBrush GreenBrush = Brush("#12B76A");
        private static readonly SolidColorBrush BlueBrush = Brush("#3366FF");
        private static readonly SolidColorBrush LineBrush = Brush("#E4E7EC");

        private readonly CodexAuthService _auth;
        private readonly SettingsStore _settingsStore;
        private readonly AppSettings _settings;
        private readonly bool _startHidden;
        private readonly StackPanel _accountList;
        private readonly TextBlock _statusText;
        private readonly TextBlock _summaryText;
        private Button _refreshButton;
        private CheckBox _liveUsageCheck;
        private CheckBox _startupCheck;
        private readonly System.Windows.Forms.NotifyIcon _tray;
        private readonly System.Drawing.Icon _appIcon;
        private IList<AccountInfo> _accounts = new List<AccountInfo>();
        private bool _busy;
        private bool _allowClose;

        public MainWindow(CodexAuthService auth, bool startHidden)
        {
            _auth = auth;
            _startHidden = startHidden;
            _settingsStore = new SettingsStore();
            _settings = _settingsStore.Load();
            _settings.StartWithWindows = StartupManager.IsEnabled();

            Title = "Codex Account Switcher";
            Width = 600;
            Height = 760;
            MinWidth = 480;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = PageBrush;
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            ResizeMode = ResizeMode.CanResize;
            _appIcon = LoadApplicationIcon();
            if (_appIcon != null)
            {
                Icon = Imaging.CreateBitmapSourceFromHIcon(_appIcon.Handle, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(BuildHeader());

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(26, 24, 26, 20)
            };
            StackPanel content = new StackPanel();
            _summaryText = new TextBlock
            {
                Text = L.T("계정을 불러오는 중입니다…", "Loading accounts…"),
                Foreground = MutedBrush,
                FontSize = 14,
                Margin = new Thickness(2, 0, 0, 14)
            };
            content.Children.Add(_summaryText);
            _accountList = new StackPanel();
            content.Children.Add(_accountList);
            content.Children.Add(BuildAddAccountCard());
            content.Children.Add(BuildSettingsCard());
            scroll.Content = content;
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            Border status = new Border
            {
                Background = CardBrush,
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(26, 13, 26, 13)
            };
            _statusText = new TextBlock
            {
                Text = L.T("준비됨", "Ready"),
                Foreground = MutedBrush,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            status.Child = _statusText;
            Grid.SetRow(status, 2);
            root.Children.Add(status);
            Content = root;

            _tray = CreateTrayIcon();
            Closing += OnClosing;
            Loaded += async delegate
            {
                if (_startHidden) Hide();
                await RefreshAccountsAsync();
            };
        }

        private UIElement BuildHeader()
        {
            Border header = new Border
            {
                Background = Brush("#101828"),
                Padding = new Thickness(28, 24, 24, 23)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(new TextBlock
            {
                Text = L.T("Codex 계정 스위처", "Codex Account Switcher"),
                Foreground = Brushes.White,
                FontSize = 25,
                FontWeight = FontWeights.SemiBold
            });
            title.Children.Add(new TextBlock
            {
                Text = L.T("세션은 그대로, 로그인 계정만 안전하게 전환",
                    "Switch login accounts safely without changing sessions"),
                Foreground = Brush("#98A2B3"),
                FontSize = 13,
                Margin = new Thickness(0, 5, 0, 0)
            });
            grid.Children.Add(title);
            _refreshButton = MakeButton(L.T("새로고침", "Refresh"), false);
            _refreshButton.Margin = new Thickness(16, 3, 0, 0);
            _refreshButton.Click += async delegate { await RefreshAccountsAsync(); };
            Grid.SetColumn(_refreshButton, 1);
            grid.Children.Add(_refreshButton);
            header.Child = grid;
            return header;
        }

        private UIElement BuildAddAccountCard()
        {
            Border card = MakeCard();
            card.Margin = new Thickness(0, 4, 0, 14);
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = L.T("새 계정 등록", "Add account"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            });
            panel.Children.Add(new TextBlock
            {
                Text = L.T("로그인은 별도 창에서 진행됩니다. 완료 후 이 화면이 자동으로 갱신됩니다.",
                    "Sign-in opens in a separate window. This page refreshes automatically when finished."),
                FontSize = 12,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 5, 0, 13),
                TextWrapping = TextWrapping.Wrap
            });
            WrapPanel buttons = new WrapPanel();
            Button browser = MakeButton(L.T("브라우저로 로그인", "Sign in with browser"), true);
            browser.Margin = new Thickness(0, 0, 8, 8);
            browser.Click += delegate { StartLogin(false); };
            buttons.Children.Add(browser);
            Button device = MakeButton(L.T("기기 코드로 로그인", "Sign in with device code"), false);
            device.Margin = new Thickness(0, 0, 8, 8);
            device.Click += delegate { StartLogin(true); };
            buttons.Children.Add(device);
            panel.Children.Add(buttons);
            card.Child = panel;
            return card;
        }

        private UIElement BuildSettingsCard()
        {
            Border card = MakeCard();
            card.Margin = new Thickness(0, 0, 0, 8);
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = L.T("설정", "Settings"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush,
                Margin = new Thickness(0, 0, 0, 11)
            });
            _startupCheck = new CheckBox
            {
                Content = L.T("Windows 로그인 시 트레이에서 자동 시작",
                    "Start in the system tray when I sign in to Windows"),
                IsChecked = _settings.StartWithWindows,
                Foreground = TextBrush,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 11)
            };
            _startupCheck.Checked += delegate { ChangeStartup(true); };
            _startupCheck.Unchecked += delegate { ChangeStartup(false); };
            panel.Children.Add(_startupCheck);

            _liveUsageCheck = new CheckBox
            {
                Content = L.T("실시간 사용량 조회 (선택 사항)", "Live usage lookup (optional)"),
                IsChecked = _settings.UseApiUsage,
                Foreground = TextBrush,
                FontSize = 13,
                Margin = new Thickness(0, 2, 0, 3)
            };
            _liveUsageCheck.Checked += async delegate
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    L.T("실시간 조회는 제3자 도구 codex-auth가 현재 계정의 토큰으로 OpenAI 사용량 API를 호출합니다. 이 기능을 켤까요?",
                        "Live lookup lets the third-party codex-auth tool call the OpenAI usage API with the current account token. Enable it?"),
                    L.T("실시간 사용량 조회", "Live usage lookup"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    _liveUsageCheck.IsChecked = false;
                    return;
                }
                _settings.UseApiUsage = true;
                SaveSettings();
                await RefreshAccountsAsync();
            };
            _liveUsageCheck.Unchecked += async delegate
            {
                _settings.UseApiUsage = false;
                SaveSettings();
                if (IsLoaded) await RefreshAccountsAsync();
            };
            panel.Children.Add(_liveUsageCheck);
            panel.Children.Add(new TextBlock
            {
                Text = L.T("기본값은 로컬 정보만 사용하며 외부 사용량 요청을 보내지 않습니다.",
                    "By default, only local data is used and no external usage request is sent."),
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(23, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            card.Child = panel;
            return card;
        }

        private Border MakeAccountCard(AccountInfo account)
        {
            Border card = MakeCard();
            card.Margin = new Thickness(0, 0, 0, 12);
            Grid outer = new Grid();
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new StackPanel();
            WrapPanel label = new WrapPanel();
            label.Children.Add(new TextBlock
            {
                Text = account.DisplayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 16,
                Foreground = TextBrush,
                Margin = new Thickness(0, 0, 8, 0)
            });
            if (account.IsActive)
            {
                Border badge = new Border
                {
                    Background = Brush("#ECFDF3"),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(8, 2, 8, 2),
                    Child = new TextBlock { Text = L.T("사용 중", "Active"), Foreground = GreenBrush, FontSize = 11, FontWeight = FontWeights.SemiBold }
                };
                label.Children.Add(badge);
            }
            identity.Children.Add(label);
            identity.Children.Add(new TextBlock
            {
                Text = L.F("{0}  ·  #{1}  ·  최근 활동 {2}", "{0}  ·  #{1}  ·  Last active {2}",
                    account.Plan, account.Selector, account.LastActivity),
                Foreground = MutedBrush,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0)
            });
            top.Children.Add(identity);
            Button switchButton = MakeButton(account.IsActive
                ? L.T("현재 계정", "Current account")
                : L.T("이 계정으로 전환", "Switch to this account"), !account.IsActive);
            switchButton.IsEnabled = !account.IsActive && !_busy;
            switchButton.Margin = new Thickness(14, 0, 0, 0);
            switchButton.Tag = account;
            switchButton.Click += async delegate(object sender, RoutedEventArgs e)
            {
                await SwitchAccountAsync((AccountInfo)((Button)sender).Tag);
            };
            Grid.SetColumn(switchButton, 1);
            top.Children.Add(switchButton);
            outer.Children.Add(top);

            Grid usage = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            usage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            usage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            usage.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            usage.Children.Add(MakeUsage(L.T("5시간 사용량", "5-hour usage"), account.FiveHourUsage, account.FiveHourUsedPercent));
            UIElement weekly = MakeUsage(L.T("주간 사용량", "Weekly usage"), account.WeeklyUsage, account.WeeklyUsedPercent);
            Grid.SetColumn(weekly, 2);
            usage.Children.Add(weekly);
            Grid.SetRow(usage, 1);
            outer.Children.Add(usage);
            card.Child = outer;
            return card;
        }

        private UIElement MakeUsage(string label, string text, int? percent)
        {
            StackPanel panel = new StackPanel();
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Foreground = MutedBrush, FontSize = 11 });
            TextBlock value = new TextBlock { Text = text, Foreground = TextBrush, FontSize = 11, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            panel.Children.Add(row);
            ProgressBar bar = new ProgressBar
            {
                Height = 6,
                Minimum = 0,
                Maximum = 100,
                Value = percent.HasValue ? percent.Value : 0,
                Foreground = percent.HasValue && percent.Value >= 80 ? Brush("#F79009") : BlueBrush,
                Background = Brush("#EAECF0"),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 7, 0, 0)
            };
            panel.Children.Add(bar);
            return panel;
        }

        private async Task RefreshAccountsAsync()
        {
            if (_busy) return;
            SetBusy(true, L.T("계정 정보를 확인하는 중…", "Checking account information…"));
            try
            {
                if (!_auth.IsAvailable)
                {
                    ShowEmpty(L.T("codex-auth가 설치되어 있지 않습니다. 아래 설치 안내를 먼저 실행해 주세요.",
                        "codex-auth is not installed. Follow the installation instructions first."));
                    return;
                }
                _accounts = await _auth.ListAccountsAsync(_settings.UseApiUsage);
                RenderAccounts();
                SetStatus(_accounts.Count == 0
                    ? L.T("등록된 계정이 없습니다.", "No accounts are registered.")
                    : L.T("계정 정보를 새로 불러왔습니다.", "Account information refreshed."));
            }
            catch (Exception ex)
            {
                ShowEmpty(L.T("계정 정보를 읽지 못했습니다. ", "Could not read account information. ") + ex.Message);
                SetStatus(L.T("새로고침 실패", "Refresh failed"));
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void RenderAccounts()
        {
            _accountList.Children.Clear();
            AccountInfo active = _accounts.FirstOrDefault(a => a.IsActive);
            _summaryText.Text = _accounts.Count == 0
                ? L.T("등록된 계정이 없습니다.", "No accounts are registered.")
                : L.F("{0}개 계정 · 현재 {1}", "{0} account(s) · Current: {1}",
                    _accounts.Count, active == null ? L.T("알 수 없음", "Unknown") : active.DisplayName);
            foreach (AccountInfo account in _accounts) _accountList.Children.Add(MakeAccountCard(account));
            RebuildTrayMenu();
        }

        private void ShowEmpty(string message)
        {
            _accounts = new List<AccountInfo>();
            _accountList.Children.Clear();
            Border card = MakeCard();
            card.Margin = new Thickness(0, 0, 0, 12);
            card.Child = new TextBlock { Text = message, Foreground = MutedBrush, FontSize = 13, TextWrapping = TextWrapping.Wrap };
            _accountList.Children.Add(card);
            _summaryText.Text = L.T("계정 연결이 필요합니다", "Account connection required");
            RebuildTrayMenu();
        }

        private async Task SwitchAccountAsync(AccountInfo target)
        {
            if (_busy || target == null || target.IsActive) return;
            if (_settings.ConfirmBeforeSwitch)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    L.F("{0} 계정으로 전환할까요?\n\nCodex를 종료하고 인증을 바꾼 뒤 다시 실행합니다. 열려 있는 작업 내용은 먼저 저장해 주세요.",
                        "Switch to {0}?\n\nCodex will close, switch authentication, and restart. Save any open work first.",
                        target.DisplayName),
                    L.T("계정 전환", "Switch account"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }

            string cliPids = _auth.IsDemoMode ? null : CodexAppManager.FindExternalCliProcessDescription();
            if (!string.IsNullOrEmpty(cliPids))
            {
                MessageBox.Show(this,
                    L.F("별도로 실행 중인 Codex 명령줄 작업이 있습니다 (PID {0}). 먼저 종료한 뒤 다시 시도해 주세요.",
                        "A separate Codex command-line task is running (PID {0}). Close it and try again.", cliPids),
                    L.T("전환을 잠시 멈췄습니다", "Switch paused"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AccountInfo previous = _accounts.FirstOrDefault(a => a.IsActive);
            SetBusy(true, L.T("Codex를 종료하고 계정을 전환하는 중…",
                "Closing Codex and switching accounts…"));
            bool codexStopped = false;
            try
            {
                if (!_auth.IsDemoMode)
                {
                    await Task.Run(() => CodexAppManager.StopCodexDesktop());
                    codexStopped = true;
                }

                CommandResult switched = await _auth.SwitchAsync(target.SwitchKey);
                if (!switched.Success)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(switched.Output)
                        ? L.T("codex-auth 전환 명령이 실패했습니다.", "The codex-auth switch command failed.")
                        : switched.Output);

                IList<AccountInfo> verified = await _auth.ListAccountsAsync(false);
                AccountInfo now = verified.FirstOrDefault(a => a.IsActive);
                if (now == null || !string.Equals(now.Email, target.Email, StringComparison.OrdinalIgnoreCase))
                {
                    bool rolledBack = false;
                    if (previous != null)
                    {
                        CommandResult rollback = await _auth.SwitchAsync(previous.SwitchKey);
                        rolledBack = rollback.Success;
                    }
                    throw new InvalidOperationException(rolledBack
                        ? L.T("대상 계정 확인에 실패해 이전 계정으로 자동 복구했습니다.",
                            "Target account verification failed, so the previous account was restored automatically.")
                        : L.T("대상 계정 확인에 실패했고 자동 복구도 완료하지 못했습니다. codex-auth list로 현재 계정을 확인해 주세요.",
                            "Target account verification failed and automatic recovery did not complete. Check the current account with codex-auth list."));
                }

                _accounts = verified;
                RenderAccounts();
                SetStatus(L.F("{0} 계정으로 전환했습니다.", "Switched to {0}.", target.DisplayName));
                if (!_auth.IsDemoMode) CodexAppManager.LaunchCodexDesktop();
                MessageBox.Show(this,
                    L.F("{0} 계정으로 전환했습니다. Codex를 다시 실행합니다.",
                        "Switched to {0}. Codex will now restart.", target.DisplayName),
                    L.T("전환 완료", "Switch complete"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (codexStopped)
                {
                    try { CodexAppManager.LaunchCodexDesktop(); }
                    catch { }
                }
                SetStatus(L.T("계정 전환 실패", "Account switch failed"));
                MessageBox.Show(this, ex.Message, L.T("계정 전환 실패", "Account switch failed"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
                System.Windows.Threading.DispatcherOperation pendingRefresh =
                    Dispatcher.BeginInvoke(new Action(async delegate { await RefreshAccountsAfterFailureAsync(); }));
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task RefreshAccountsAfterFailureAsync()
        {
            try
            {
                _accounts = await _auth.ListAccountsAsync(false);
                RenderAccounts();
            }
            catch { }
        }

        private void StartLogin(bool deviceCode)
        {
            if (_busy) return;
            if (!_auth.IsAvailable)
            {
                MessageBox.Show(this,
                    L.T("codex-auth를 찾지 못했습니다. 설치 안내를 먼저 확인해 주세요.",
                        "codex-auth was not found. Check the installation instructions first."),
                    L.T("도구 없음", "Tool not found"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_auth.IsDemoMode)
            {
                MessageBox.Show(this,
                    L.T("미리보기 모드에서는 로그인 창을 열지 않습니다.",
                        "The sign-in window is unavailable in preview mode."),
                    L.T("미리보기", "Preview"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult answer = MessageBox.Show(this,
                L.T("계정 등록 중 인증 파일 충돌을 막기 위해 Codex를 종료합니다. 로그인 완료 후 Codex를 다시 실행할까요?",
                    "Codex will close to prevent authentication conflicts while adding the account. Restart Codex after sign-in?"),
                L.T("새 계정 등록", "Add account"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                string bundledCliDirectory = CodexAppManager.FindBundledCliDirectory();
                CodexAppManager.StopCodexDesktop();
                Process login = _auth.StartLogin(deviceCode, bundledCliDirectory);
                SetStatus(L.T("로그인 창에서 계정 등록을 완료해 주세요.",
                    "Complete account registration in the sign-in window."));
                if (login != null)
                {
                    login.EnableRaisingEvents = true;
                    login.Exited += delegate
                    {
                        Dispatcher.BeginInvoke(new Action(async delegate
                        {
                            try { CodexAppManager.LaunchCodexDesktop(); } catch { }
                            await RefreshAccountsAsync();
                            ShowAndActivate();
                        }));
                    };
                }
            }
            catch (Exception ex)
            {
                try { CodexAppManager.LaunchCodexDesktop(); } catch { }
                MessageBox.Show(this, ex.Message, L.T("로그인 시작 실패", "Could not start sign-in"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChangeStartup(bool enabled)
        {
            if (!IsLoaded) return;
            try
            {
                StartupManager.SetEnabled(enabled);
                _settings.StartWithWindows = enabled;
                SaveSettings();
                SetStatus(enabled
                    ? L.T("Windows 자동 시작을 켰습니다.", "Windows startup enabled.")
                    : L.T("Windows 자동 시작을 껐습니다.", "Windows startup disabled."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.T("자동 시작 설정 실패", "Startup setting failed"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _refreshButton.IsEnabled = !busy;
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
            if (_accounts.Count > 0) RenderAccounts();
            Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        }

        private void SetStatus(string message)
        {
            _statusText.Text = message;
            _tray.Text = message.Length > 63 ? message.Substring(0, 63) : message;
        }

        private void SaveSettings()
        {
            try { _settingsStore.Save(_settings); }
            catch (Exception ex) { SetStatus(L.T("설정을 저장하지 못했습니다: ", "Could not save settings: ") + ex.Message); }
        }

        private System.Windows.Forms.NotifyIcon CreateTrayIcon()
        {
            var tray = new System.Windows.Forms.NotifyIcon
            {
                Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
                Text = "Codex Account Switcher",
                Visible = true
            };
            tray.DoubleClick += delegate { ShowAndActivate(); };
            RebuildTrayMenu(tray);
            return tray;
        }

        private void RebuildTrayMenu()
        {
            RebuildTrayMenu(_tray);
        }

        private void RebuildTrayMenu(System.Windows.Forms.NotifyIcon tray)
        {
            if (tray == null) return;
            var menu = new System.Windows.Forms.ContextMenuStrip();
            var open = new System.Windows.Forms.ToolStripMenuItem(L.T("스위처 열기", "Open switcher"));
            open.Font = new System.Drawing.Font(open.Font, System.Drawing.FontStyle.Bold);
            open.Click += delegate { Dispatcher.BeginInvoke(new Action(ShowAndActivate)); };
            menu.Items.Add(open);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            foreach (AccountInfo account in _accounts)
            {
                var item = new System.Windows.Forms.ToolStripMenuItem((account.IsActive ? "● " : "   ") + account.DisplayName);
                item.Enabled = !account.IsActive && !_busy;
                AccountInfo captured = account;
                item.Click += delegate { Dispatcher.BeginInvoke(new Action(async delegate { ShowAndActivate(); await SwitchAccountAsync(captured); })); };
                menu.Items.Add(item);
            }
            if (_accounts.Count > 0) menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var refresh = new System.Windows.Forms.ToolStripMenuItem(L.T("새로고침", "Refresh"));
            refresh.Click += delegate { Dispatcher.BeginInvoke(new Action(async delegate { await RefreshAccountsAsync(); })); };
            menu.Items.Add(refresh);
            var quit = new System.Windows.Forms.ToolStripMenuItem(L.T("종료", "Quit"));
            quit.Click += delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _allowClose = true;
                    Close();
                }));
            };
            menu.Items.Add(quit);
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
        }

        private void ShowAndActivate()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(1200, "Codex Account Switcher",
                    L.T("트레이에서 계속 실행 중입니다.", "Still running in the system tray."),
                    System.Windows.Forms.ToolTipIcon.Info);
                return;
            }
            _tray.Visible = false;
            _tray.Dispose();
            if (_appIcon != null) _appIcon.Dispose();
        }

        private static Border MakeCard()
        {
            return new Border
            {
                Background = CardBrush,
                BorderBrush = LineBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(19),
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.06, Color = Colors.Black }
            };
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new Button
            {
                Content = text,
                MinHeight = 36,
                Padding = new Thickness(15, 6, 15, 6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Foreground = primary ? Brushes.White : TextBrush,
                Background = primary ? BlueBrush : Brush("#F2F4F7"),
                BorderBrush = primary ? BlueBrush : LineBrush,
                BorderThickness = new Thickness(1)
            };
            return button;
        }

        private static SolidColorBrush Brush(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private static System.Drawing.Icon LoadApplicationIcon()
        {
            try
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);
            }
            catch
            {
                return null;
            }
        }
    }
}
