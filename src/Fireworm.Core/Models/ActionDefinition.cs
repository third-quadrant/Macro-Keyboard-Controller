using System.Text.Json.Serialization;

namespace Fireworm.Core.Models;

/// <summary>
/// A single user-visible action. The action engine never knows which physical key produced it.
/// </summary>
public sealed class ActionDefinition
{
    public string Type { get; set; } = "none"; // none | keyboard | text | launch | media | mouse
    public List<string> Keys { get; set; } = new();
    public string Text { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string Media { get; set; } = "volume_up";
    public string Mouse { get; set; } = "left_click";
    public int WheelDelta { get; set; } = 120;

    [JsonIgnore]
    public bool IsNone => string.IsNullOrWhiteSpace(Type) || Type.Equals("none", StringComparison.OrdinalIgnoreCase);

    public ActionDefinition Clone() => new()
    {
        Type = Type,
        Keys = new List<string>(Keys),
        Text = Text,
        Path = Path,
        Arguments = Arguments,
        Media = Media,
        Mouse = Mouse,
        WheelDelta = WheelDelta,
    };

    public string Summary()
    {
        if (IsNone) return "설정 안 됨";
        return Type.ToLowerInvariant() switch
        {
            "keyboard" => Keys.Count == 0 ? "키 입력 (미설정)" : "키: " + string.Join("  ", Keys),
            "text" => string.IsNullOrEmpty(Text) ? "텍스트 (미설정)" : "텍스트: " + Truncate(Text, 24),
            "launch" => string.IsNullOrEmpty(Path) ? "프로그램 (경로 미설정)" : "실행: " + Truncate(Path, 28),
            "media" => "미디어: " + MediaLabel(Media),
            "mouse" => "마우스: " + MouseLabel(Mouse),
            _ => Type + " (알 수 없음)",
        };
    }

    public static string MediaLabel(string media) => media switch
    {
        "volume_up" => "볼륨 증가",
        "volume_down" => "볼륨 감소",
        "mute" => "음소거",
        "play_pause" => "재생/일시정지",
        "next" => "다음 트랙",
        "prev" => "이전 트랙",
        _ => media,
    };

    public static string MouseLabel(string mouse) => mouse switch
    {
        "left_click" => "왼쪽 클릭",
        "right_click" => "오른쪽 클릭",
        "middle_click" => "가운데 클릭",
        "wheel_up" => "스크롤 위",
        "wheel_down" => "스크롤 아래",
        _ => mouse,
    };

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}
