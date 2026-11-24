using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Fireworm.Core.Actions;
using Fireworm.Core.Device;
using Fireworm.Core.Input;
using Fireworm.Core.Models;
using Fireworm.Core.Settings;

namespace Fireworm.App;

public partial class DeviceTriggerWindow : Window
{
    private readonly DeviceService _device;
    private readonly SettingsStore _store;
    private readonly RawInputService _raw;
    private readonly ObservableCollection<string> _backupLines = new();
    private readonly ObservableCollection<string> _verifyLines = new();
    private readonly HashSet<TriggerId> _verified = new();

    private DeviceBackup _backup;
    private Queue<TriggerId> _recordQueue = new();
    private TriggerId? _recordingTrigger;
    private bool _recording;
    private bool _busy;
    private bool _awaitingKey1;
    private bool _key1Verified;
    private bool _verifying;
    private DispatcherTimer? _key1Timer;

    public DeviceTriggerWindow(DeviceService device, SettingsStore store, RawInputService raw)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Trace("ctor-after-initialize");
        _device = device;
        _store = store;
        _raw = raw;
        _backup = _store.LoadOrCreateBackup();

        BackupList.ItemsSource = _backupLines;
        VerifyList.ItemsSource = _verifyLines;

        _raw.TriggerReceived += HandleTrigger;
        _raw.ObservationReceived += HandleObservation;
        _device.StatusChanged += Device_StatusChanged;

        Loaded += (_, _) =>
        {
            Trace($"loaded visible={IsVisible} size={ActualWidth}x{ActualHeight}");
            UpdateDevice();
            RefreshBackupList();
            UpdateVerifyList();
        };
        Closed += (_, _) =>
        {
            _raw.TriggerReceived -= HandleTrigger;
            _raw.ObservationReceived -= HandleObservation;
            _device.StatusChanged -= Device_StatusChanged;
            _key1Timer?.Stop();
        };
    }

    private void Device_StatusChanged(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(new Action(UpdateDevice));

    private void UpdateDevice()
    {
        DeviceStatusText.Text = _device.IsConnected ? "● 장치 연결됨" : "● 장치 연결 안 됨";
        DeviceStatusText.Foreground = _device.IsConnected
            ? (System.Windows.Media.Brush)FindResource("Green")
            : (System.Windows.Media.Brush)FindResource("Red");
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var connected = _device.IsConnected;
        ApplyKey1Button.IsEnabled = connected && !_busy;
        ApplyRestButton.IsEnabled = connected && !_busy && _key1Verified && !_verifying;
        RestoreButton.IsEnabled = connected && !_busy && HasAllBackup();
        ConfirmCheck.IsEnabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtons();
    }

    private void RefreshBackupList()
    {
        _backupLines.Clear();
        BackupPathText.Text = "백업 파일: " + _store.BackupPath;
        foreach (var key in _backup.Keys.OrderBy(k => k.Slot))
            _backupLines.Add($"{key.Key}: {key.Description}  (usage 0x{key.Usage:X2}, mod 0x{key.Modifier:X2})");
        if (_backup.Keys.Count == 0)
            _backupLines.Add("백업이 없습니다. '기본 백업 저장' 또는 '현재 동작 녹음'을 먼저 실행하세요.");
        UpdateButtons();
    }

    private void UpdateVerifyList()
    {
        _verifyLines.Clear();
        foreach (var trigger in TriggerCatalog.Keys)
        {
            var state = _verified.Contains(trigger) ? "확인됨" : "대기";
            var mark = _verified.Contains(trigger) ? "✔" : "·";
            _verifyLines.Add($"{mark} {TriggerCatalog.DisplayName(trigger)} → {TriggerCatalog.InternalTriggerLabel(trigger),-4}  [{state}]");
        }
    }

    private void SaveDefaultBackup_Click(object sender, RoutedEventArgs e)
    {
        _backup = DeviceBackup.CreateObservedDefault();
        _store.SaveBackup(_backup);
        RefreshBackupList();
        RecordStatusText.Text = "기본 백업을 저장했습니다 (KEY1=A, KEY2~6=C). 실제 현재 동작과 다르면 '현재 동작 녹음'을 사용하세요.";
    }

    private async void RecordBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected)
        {
            MessageBox.Show(this, "장치를 먼저 연결하세요.", "장치 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_recording)
        {
            RecordStatusText.Text = "이미 녹음 중입니다.";
            return;
        }

        var result = MessageBox.Show(this,
            "현재 KEY1~KEY6의 동작을 순서대로 기록합니다. 각 키를 안내에 따라 한 번씩 눌러주세요.\n녹음은 읽기 전용이며 장치를 변경하지 않습니다.\n\n시작할까요?",
            "현재 동작 녹음", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _backup = new DeviceBackup { Note = "Recorded from current device behaviour." };
        _recordQueue = new Queue<TriggerId>(TriggerCatalog.Keys);
        _recording = true;
        NextRecordPrompt();
        await Task.CompletedTask;
    }

    private void NextRecordPrompt()
    {
        if (_recordQueue.Count == 0)
        {
            _recording = false;
            _recordingTrigger = null;
            _store.SaveBackup(_backup);
            RefreshBackupList();
            RecordStatusText.Text = "녹음 완료. 백업이 저장되었습니다.";
            return;
        }

        _recordingTrigger = _recordQueue.Dequeue();
        RecordStatusText.Text = $"{TriggerCatalog.DisplayName(_recordingTrigger.Value)}을(를) 한 번 눌러주세요…";
    }

    private void HandleObservation(RawObservation observation)
    {
        if (!_recording || _recordingTrigger is null) return;
        if (observation.Kind != RawObservationKind.Keyboard) return;

        // Never record the internal trigger keys as a backup value.
        if (observation.VKey is >= 0x7C and <= 0x81)
        {
            RecordStatusText.Text = "내부 트리거(F13~F18)는 백업 값으로 기록하지 않습니다. KEY1~KEY6을 눌러주세요.";
            return;
        }

        if (!KeyParser.TryVKeyToHidUsage(observation.VKey, out var usage))
        {
            RecordStatusText.Text = $"이 키(VKey 0x{observation.VKey:X2})는 복원용 usage로 변환할 수 없습니다. 다른 키를 누르거나 기본 백업을 사용하세요.";
            return;
        }

        var trigger = _recordingTrigger.Value;
        _backup.Keys.Add(new KeyBackup
        {
            Slot = TriggerCatalog.KeySlot(trigger),
            Key = TriggerCatalog.SettingKey(trigger),
            Type = "keyboard",
            Usage = usage,
            Modifier = 0,
            Description = KeyParser.KeyName(observation.VKey),
        });
        NextRecordPrompt();
    }

    private void HandleTrigger(TriggerEvent trigger)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => HandleTrigger(trigger)));
            return;
        }

        if (_awaitingKey1 && trigger.Trigger == TriggerId.Key1)
        {
            _awaitingKey1 = false;
            _key1Timer?.Stop();
            _key1Verified = true;
            _verified.Add(TriggerId.Key1);
            ApplyStatusText.Text = "KEY1 → F13 확인됨. 이제 '나머지 5개 적용'을 누르세요.";
            UpdateVerifyList();
            UpdateButtons();
            return;
        }

        if (_verifying && TriggerCatalog.IsKey(trigger.Trigger))
        {
            _verified.Add(trigger.Trigger);
            UpdateVerifyList();
            if (TriggerCatalog.Keys.All(_verified.Contains))
            {
                _verifying = false;
                ApplyStatusText.Text = "모든 키가 확인되었습니다. 실제 버튼으로 동작을 사용할 수 있습니다.";
                UpdateButtons();
            }
        }
    }

    private bool ValidateApply(bool requireAllBackup)
    {
        if (!_device.IsConnected)
        {
            MessageBox.Show(this, "장치가 연결되어 있지 않습니다.", "장치 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (ConfirmCheck.IsChecked != true)
        {
            MessageBox.Show(this, "먼저 백업 확인 및 변경 동의에 체크하세요.", "확인 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_backup.ForSlot(1) is null || (requireAllBackup && !HasAllBackup()))
        {
            MessageBox.Show(this, "백업이 완전하지 않습니다. 먼저 백업을 저장하거나 녹음하세요.", "백업 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        return true;
    }

    private bool HasAllBackup() => TriggerCatalog.Keys.All(k => _backup.ForSlot(TriggerCatalog.KeySlot(k)) is not null);

    private async void ApplyKey1_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateApply(requireAllBackup: false)) return;
        var backup = _backup.ForSlot(1)!;
        var message =
            $"KEY1의 현재 동작: {backup.Description} (usage 0x{backup.Usage:X2}, mod 0x{backup.Modifier:X2})\n" +
            "변경: KEY1 → 내부 트리거 F13\n\n" +
            "적용 후 KEY1을 눌러 F13이 감지되는지 확인합니다. 10초 안에 감지되지 않으면 자동으로 백업 동작으로 복구합니다.\n\n" +
            "계속할까요?";
        if (MessageBox.Show(this, message, "KEY1 시험 적용", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        ApplyStatusText.Text = "KEY1에 F13을 쓰는 중…";
        try
        {
            await Task.Run(() =>
            {
                using var writer = _device.OpenWriter();
                writer.WriteKeySlot(1, TriggerCatalog.TriggerUsage(TriggerId.Key1), 0, 1);
            });

            _key1Verified = false;
            _awaitingKey1 = true;
            _verified.Clear();
            UpdateVerifyList();
            ApplyStatusText.Text = "KEY1을 눌러 F13이 감지되는지 확인하세요. 10초 안에 감지되지 않으면 자동 복구합니다.";
            StartKey1Timer();
        }
        catch (Exception ex)
        {
            ApplyStatusText.Text = "KEY1 적용 실패: " + ex.Message;
            MessageBox.Show(this, "KEY1 적용에 실패했습니다.\n\n" + ex.Message, "적용 실패",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void StartKey1Timer()
    {
        _key1Timer?.Stop();
        _key1Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _key1Timer.Tick += async (_, _) =>
        {
            _key1Timer?.Stop();
            if (_awaitingKey1)
                await RestoreKey1Async("F13이 10초 안에 감지되지 않았습니다.");
        };
        _key1Timer.Start();
    }

    private async Task RestoreKey1Async(string reason)
    {
        var backup = _backup.ForSlot(1);
        if (backup is null) return;

        SetBusy(true);
        ApplyStatusText.Text = reason + " KEY1을 백업 동작으로 복구하는 중…";
        try
        {
            await Task.Run(() =>
            {
                using var writer = _device.OpenWriter();
                writer.WriteKeySlot(1, backup.Usage, backup.Modifier, 1);
            });
            _awaitingKey1 = false;
            _key1Verified = false;
            _verified.Remove(TriggerId.Key1);
            UpdateVerifyList();
            ApplyStatusText.Text = reason + " KEY1을 백업 동작으로 복구했습니다.";
        }
        catch (Exception ex)
        {
            ApplyStatusText.Text = "KEY1 자동 복구 실패: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ApplyRest_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateApply(requireAllBackup: true)) return;
        if (!_key1Verified)
        {
            MessageBox.Show(this, "먼저 KEY1 시험 적용으로 F13 감지를 확인하세요.", "KEY1 확인 필요",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = string.Join("\n", TriggerCatalog.Keys.Select(t =>
            $"{TriggerCatalog.DisplayName(t)} → {TriggerCatalog.InternalTriggerLabel(t)}"));
        if (MessageBox.Show(this,
                "다음 변경을 적용합니다.\n\n" + preview + "\n\n복구는 '전체 복구'로 수행할 수 있습니다. 계속할까요?",
                "나머지 키 적용", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        ApplyStatusText.Text = "KEY2~KEY6에 내부 트리거를 쓰는 중…";
        try
        {
            await Task.Run(() =>
            {
                using var writer = _device.OpenWriter();
                foreach (var trigger in TriggerCatalog.Keys.Skip(1))
                    writer.WriteKeySlot(TriggerCatalog.KeySlot(trigger), TriggerCatalog.TriggerUsage(trigger), 0, 1);
            });

            _verifying = true;
            _verified.Clear();
            _verified.Add(TriggerId.Key1);
            UpdateVerifyList();
            ApplyStatusText.Text = "KEY2~KEY6을 하나씩 눌러 각 내부 트리거가 감지되는지 확인하세요.";
        }
        catch (Exception ex)
        {
            ApplyStatusText.Text = "적용 실패: " + ex.Message;
            MessageBox.Show(this, "적용에 실패했습니다. '전체 복구'를 실행하세요.\n\n" + ex.Message,
                "적용 실패", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!_device.IsConnected)
        {
            MessageBox.Show(this, "장치가 연결되어 있지 않습니다.", "장치 없음", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!HasAllBackup())
        {
            MessageBox.Show(this, "전체 복구에 필요한 KEY1~KEY6 백업이 없습니다. 먼저 백업을 저장하거나 녹음하세요.",
                "백업 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show(this,
                "KEY1~KEY6을 백업 파일의 동작으로 복구합니다. 계속할까요?\n\n" +
                string.Join("\n", _backup.Keys.OrderBy(k => k.Slot).Select(k => $"{k.Key}: {k.Description} (0x{k.Usage:X2})")),
                "전체 복구", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        ApplyStatusText.Text = "백업 동작으로 복구하는 중…";
        try
        {
            await Task.Run(() =>
            {
                using var writer = _device.OpenWriter();
                writer.WriteRestore(_backup, 1);
            });
            _awaitingKey1 = false;
            _key1Verified = false;
            _verifying = false;
            _verified.Clear();
            _key1Timer?.Stop();
            UpdateVerifyList();
            ApplyStatusText.Text = "복구 완료. 각 키를 눌러 백업한 동작이 나오는지 확인하세요.";
        }
        catch (Exception ex)
        {
            ApplyStatusText.Text = "복구 실패: " + ex.Message;
            MessageBox.Show(this, "복구에 실패했습니다.\n\n" + ex.Message, "복구 실패",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static void Trace(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fireworm");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "fireworm.log"), $"{DateTime.Now:O}  WIZARD {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the setup window.
        }
    }
}
