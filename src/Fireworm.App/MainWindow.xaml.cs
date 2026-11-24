using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Fireworm.Core.Actions;
using Fireworm.Core.Device;
using Fireworm.Core.Input;
using Fireworm.Core.Models;
using Fireworm.Core.Settings;
using Microsoft.Win32;

namespace Fireworm.App;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly DeviceService _device = new();
    private readonly RawInputService _raw = new();
    private readonly ActionEngine _engine = new();
    private readonly TriggerRouter _router;
    private readonly KeyLedFeedback _led;
    private readonly ObservableCollection<string> _log = new();
    private readonly Dictionary<TriggerId, TextBlock> _summaries = new();
    private readonly Dictionary<TriggerId, Button> _cards = new();

    private TriggerId _selected = TriggerId.Key1;
    private HwndSource? _source;
    private System.Threading.Timer? _pollTimer;
    private DeviceTriggerWindow? _wizard;
    private bool _loadingEditor;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _store.Load();
        _router = new TriggerRouter(_engine, trigger => _settings.GetAction(trigger), Log);
        _led = new KeyLedFeedback(() => _device.VendorPath, Log);

        _raw.TriggerReceived += OnTrigger;
        _raw.ObservationReceived += OnObservation;
        _device.StatusChanged += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateDeviceUi));

        LogList.ItemsSource = _log;

        _summaries[TriggerId.Key1] = Key1Summary;
        _summaries[TriggerId.Key2] = Key2Summary;
        _summaries[TriggerId.Key3] = Key3Summary;
        _summaries[TriggerId.Key4] = Key4Summary;
        _summaries[TriggerId.Key5] = Key5Summary;
        _summaries[TriggerId.Key6] = Key6Summary;
        _summaries[TriggerId.KnobLeft] = KnobLeftSummary;
        _summaries[TriggerId.KnobClick] = KnobClickSummary;
        _summaries[TriggerId.KnobRight] = KnobRightSummary;

        _cards[TriggerId.Key1] = Key1Card;
        _cards[TriggerId.Key2] = Key2Card;
        _cards[TriggerId.Key3] = Key3Card;
        _cards[TriggerId.Key4] = Key4Card;
        _cards[TriggerId.Key5] = Key5Card;
        _cards[TriggerId.Key6] = Key6Card;
        _cards[TriggerId.KnobLeft] = KnobLeftCard;
        _cards[TriggerId.KnobClick] = KnobClickCard;
        _cards[TriggerId.KnobRight] = KnobRightCard;

        RefreshSummaries();
        SelectTrigger(TriggerId.Key1);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);

        if (_raw.Register(hwnd))
            Log("입력 감지 시작 (Raw Input, 대상 장치만 필터링)");
        else
            Log("입력 감지 등록 실패: " + (_raw.LastError ?? "알 수 없는 오류"));

        _device.Refresh();
        UpdateDeviceUi();

        _pollTimer = new System.Threading.Timer(_ =>
        {
            _device.Refresh();
            Dispatcher.BeginInvoke(new Action(UpdateDeviceUi));
        }, null, 0, 2000);

        if (!string.IsNullOrEmpty(_store.LastWarning))
            Log(_store.LastWarning);

        LoadInstalledPrograms();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _pollTimer?.Dispose();
        _led.Dispose();
        _raw.Unregister();
        _raw.Dispose();
        if (_source != null)
            _source.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x00FF) // WM_INPUT
            _raw.ProcessMessage(lParam);
        return IntPtr.Zero;
    }

    private void OnTrigger(TriggerEvent trigger)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnTrigger(trigger)));
            return;
        }

        TriggerStatusText.Text = $"{TriggerCatalog.DisplayName(trigger.Trigger)} · {trigger.Description}";

        // While the setup wizard is open, presses are for verification only.
        // The wizard writes key packets, so the LED writer stays idle to avoid mixing packets.
        if (_wizard != null) return;

        if (TriggerCatalog.IsKey(trigger.Trigger))
        {
            var slot = TriggerCatalog.KeySlot(trigger.Trigger);
            Task.Run(() => _led.Pulse(slot));
        }

        _router.Handle(trigger);
    }

    private void OnObservation(RawObservation observation)
    {
        // The wizard subscribes directly; this hook is intentionally kept empty
        // so other devices and the normal action path are unaffected.
    }

    private void UpdateDeviceUi()
    {
        if (_device.IsConnected)
        {
            DeviceStatusPill.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x20));
            DeviceStatusDot.Fill = (Brush)FindResource("Green");
            DeviceStatusText.Text = "연결됨";
            DevicePathText.Text = "MI_01 벤더 인터페이스 · Report ID 3 · 65 bytes";
            DevicePathText.ToolTip = _device.VendorPath;
            TriggerSetupButton.IsEnabled = true;
        }
        else
        {
            DeviceStatusPill.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1B, 0x1B));
            DeviceStatusDot.Fill = (Brush)FindResource("Red");
            DeviceStatusText.Text = "연결 안 됨";
            DevicePathText.Text = "MI_01 대기";
            DevicePathText.ToolTip = null;
            TriggerSetupButton.IsEnabled = false;
        }
    }

    private void SelectTrigger(TriggerId trigger)
    {
        _selected = trigger;
        foreach (var card in _cards.Values)
        {
            card.Background = (Brush)FindResource("Panel");
            card.BorderBrush = (Brush)FindResource("Border");
        }
        _cards[trigger].Background = (Brush)FindResource("AccentDark");
        _cards[trigger].BorderBrush = (Brush)FindResource("Accent");

        EditorTitle.Text = TriggerCatalog.DisplayName(trigger) + " 동작";
        LoadActionIntoEditor(_settings.GetAction(trigger));
        EditorStatus.Text = "";
    }

    private void LoadActionIntoEditor(ActionDefinition action)
    {
        _loadingEditor = true;
        SetComboByTag(ActionTypeCombo, action.Type);
        KeyComboBox.Text = action.Keys.Count > 0 ? string.Join(" ", action.Keys) : "";
        TextValueBox.Text = action.Text;
        LaunchPathBox.Text = action.Path;
        LaunchArgsBox.Text = action.Arguments;
        SetComboByTag(MediaCombo, action.Media);
        SetComboByTag(MouseCombo, action.Mouse);
        InstalledProgramCombo.SelectedItem = null;
        UpdatePanels(action.Type);
        _loadingEditor = false;
    }

    private static void SetComboByTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";

    private void UpdatePanels(string type)
    {
        type = type.ToLowerInvariant();
        NonePanel.Visibility = type == "none" ? Visibility.Visible : Visibility.Collapsed;
        KeyboardPanel.Visibility = type == "keyboard" ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = type == "text" ? Visibility.Visible : Visibility.Collapsed;
        LaunchPanel.Visibility = type == "launch" ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = type == "media" ? Visibility.Visible : Visibility.Collapsed;
        MousePanel.Visibility = type == "mouse" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ActionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor) return;
        UpdatePanels(SelectedTag(ActionTypeCombo));
        ApplyFromEditor();
    }

    private void OptionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor) return;
        ApplyFromEditor();
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e) => ApplyFromEditor();

    private void Field_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyFromEditor();
        e.Handled = true;
    }

    private void InstalledProgramCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingEditor) return;
        if (InstalledProgramCombo.SelectedItem is not InstalledProgram program) return;
        LaunchPathBox.Text = program.Path;
        ApplyFromEditor();
    }

    private (ActionDefinition? Action, string Error) BuildAction()
    {
        var type = SelectedTag(ActionTypeCombo).ToLowerInvariant();
        var action = new ActionDefinition { Type = type };

        switch (type)
        {
            case "none":
                return (action, "");
            case "keyboard":
            {
                var text = KeyComboBox.Text.Trim();
                if (!KeyParser.TryParse(text, out _, out var error))
                    return (null, error);
                action.Keys = new List<string> { text };
                return (action, "");
            }
            case "text":
                if (string.IsNullOrEmpty(TextValueBox.Text))
                    return (null, "입력할 텍스트를 작성하세요.");
                action.Text = TextValueBox.Text;
                return (action, "");
            case "launch":
                if (string.IsNullOrWhiteSpace(LaunchPathBox.Text))
                    return (null, "프로그램 또는 파일 경로를 지정하세요.");
                action.Path = LaunchPathBox.Text.Trim();
                action.Arguments = LaunchArgsBox.Text.Trim();
                return (action, "");
            case "media":
                action.Media = SelectedTag(MediaCombo);
                return (action, "");
            case "mouse":
                action.Mouse = SelectedTag(MouseCombo);
                return (action, "");
            default:
                return (null, "동작 종류를 선택하세요.");
        }
    }

    /// <summary>Editor has no save button: every change is written immediately.</summary>
    private void ApplyFromEditor()
    {
        if (_loadingEditor) return;

        var (action, error) = BuildAction();
        if (action == null)
        {
            EditorStatus.Text = error;
            return;
        }

        _settings.SetAction(_selected, action);
        _store.Save(_settings);
        RefreshSummaries();
        EditorStatus.Text = $"{action.Summary()} · 적용됨";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "실행할 프로그램 또는 파일 선택",
            Filter = "실행 파일|*.exe;*.bat;*.cmd;*.lnk|모든 파일|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            LaunchPathBox.Text = dialog.FileName;
            ApplyFromEditor();
        }
    }

    private void LoadInstalledPrograms()
    {
        Task.Run(InstalledPrograms.Scan).ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                Dispatcher.BeginInvoke(new Action(() => Log("설치된 프로그램 목록을 불러오지 못했습니다.")));
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => InstalledProgramCombo.ItemsSource = task.Result));
        });
    }

    private void KeyCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        var trigger = TriggerFromTag(tag);
        if (trigger is not null) SelectTrigger(trigger.Value);
    }

    private void Simulate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string tag) return;
        var trigger = TriggerFromTag(tag);
        if (trigger is null) return;

        var action = _settings.GetAction(trigger.Value);
        Log($"[테스트] {TriggerCatalog.DisplayName(trigger.Value)} → {action.Summary()}");
        _router.Handle(new TriggerEvent(trigger.Value, "simulator", "테스트", DateTime.Now));
    }

    private static TriggerId? TriggerFromTag(string tag) => tag.ToUpperInvariant() switch
    {
        "KEY1" => TriggerId.Key1,
        "KEY2" => TriggerId.Key2,
        "KEY3" => TriggerId.Key3,
        "KEY4" => TriggerId.Key4,
        "KEY5" => TriggerId.Key5,
        "KEY6" => TriggerId.Key6,
        "KNOB_LEFT" => TriggerId.KnobLeft,
        "KNOB_CLICK" => TriggerId.KnobClick,
        "KNOB_RIGHT" => TriggerId.KnobRight,
        _ => null,
    };

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _device.Refresh();
        UpdateDeviceUi();
        Log("장치 상태를 새로고침했습니다: " + _device.StatusText);
    }

    private void OpenTriggerWizard_Click(object sender, RoutedEventArgs e)
    {
        if (_wizard != null) return;
        Log("트리거 설정 창 열기 요청");
        if (!_device.IsConnected)
        {
            Log("장치가 연결되어 있지 않아 창을 열지 않습니다.");
            MessageBox.Show(this, "먼저 매크로 키보드를 연결하세요.", "장치 없음",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Log("장치 연결 확인, 트리거 창 생성");
            _wizard = new DeviceTriggerWindow(_device, _store, _raw) { Owner = this };
            Log("트리거 창 생성 완료");
            _wizard.Closed += (_, _) =>
            {
                _wizard = null;
                RefreshSummaries();
                TriggerStatusText.Text = "트리거 설정 창이 닫혔습니다.";
            };
            _wizard.ShowDialog();
            Log("트리거 창 닫힘");
        }
        catch (Exception ex)
        {
            Log("트리거 창 오류: " + ex);
            _wizard = null;
            MessageBox.Show(this, "트리거 설정 창을 열지 못했습니다.\n\n" + ex.Message, "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    private void RefreshSummaries()
    {
        foreach (var trigger in TriggerCatalog.All)
        {
            if (!_summaries.TryGetValue(trigger, out var summary)) continue;

            var action = _settings.GetAction(trigger);
            // Cards show the program name; the full path lives in the tooltip.
            summary.Text = action.Type == "launch" && !string.IsNullOrEmpty(action.Path)
                ? "실행: " + Path.GetFileName(action.Path)
                : action.Summary();
            summary.ToolTip = action.Type == "launch" && !string.IsNullOrEmpty(action.Path)
                ? action.Path + (string.IsNullOrEmpty(action.Arguments) ? "" : " " + action.Arguments)
                : null;
        }
    }

    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Log(message)));
            return;
        }

        _log.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        try { File.AppendAllText(Path.Combine(_store.BaseDirectory, "fireworm.log"), $"{DateTime.Now:O}  {message}{Environment.NewLine}"); } catch { }
        while (_log.Count > 200) _log.RemoveAt(0);
        if (_log.Count > 0) LogList.ScrollIntoView(_log[^1]);
    }
}


