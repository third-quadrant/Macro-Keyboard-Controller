using System.Diagnostics;
using Fireworm.Core.Models;

namespace Fireworm.Core.Actions;

public readonly record struct ActionResult(bool Success, string Message);

public sealed class ActionEngine
{
    public ActionResult Execute(ActionDefinition action)
    {
        if (action == null) return new ActionResult(false, "동작이 null입니다.");
        if (action.IsNone) return new ActionResult(true, "설정된 동작 없음");

        try
        {
            switch (action.Type.ToLowerInvariant())
            {
                case "keyboard":
                    return ExecuteKeyboard(action);
                case "text":
                    if (string.IsNullOrEmpty(action.Text))
                        return new ActionResult(false, "텍스트가 비어 있습니다.");
                    InputSender.SendText(action.Text);
                    return new ActionResult(true, $"텍스트 입력: {Clip(action.Text)}");
                case "launch":
                    return ExecuteLaunch(action);
                case "media":
                    InputSender.SendMedia(action.Media);
                    return new ActionResult(true, "미디어: " + ActionDefinition.MediaLabel(action.Media));
                case "mouse":
                    InputSender.SendMouse(action.Mouse);
                    return new ActionResult(true, "마우스: " + ActionDefinition.MouseLabel(action.Mouse));
                default:
                    return new ActionResult(false, $"알 수 없는 동작 유형: {action.Type}");
            }
        }
        catch (Exception ex)
        {
            return new ActionResult(false, ex.Message);
        }
    }

    private static ActionResult ExecuteKeyboard(ActionDefinition action)
    {
        if (action.Keys.Count == 0)
            return new ActionResult(false, "키가 지정되지 않았습니다.");

        foreach (var key in action.Keys)
        {
            if (!KeyParser.TryParse(key, out var chord, out var error))
                return new ActionResult(false, error);
            InputSender.SendChord(chord);
        }
        return new ActionResult(true, "키 입력: " + string.Join(" ", action.Keys));
    }

    private static ActionResult ExecuteLaunch(ActionDefinition action)
    {
        if (string.IsNullOrWhiteSpace(action.Path))
            return new ActionResult(false, "프로그램 경로가 비어 있습니다.");

        var info = new ProcessStartInfo
        {
            FileName = action.Path,
            Arguments = action.Arguments ?? "",
            UseShellExecute = true,
        };
        try
        {
            var dir = Path.GetDirectoryName(action.Path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                info.WorkingDirectory = dir;
        }
        catch
        {
            // A URL or shell target may not have a directory; ShellExecute handles it.
        }

        Process.Start(info);
        return new ActionResult(true, "실행: " + action.Path);
    }

    private static string Clip(string value)
        => value.Length <= 40 ? value : value[..39] + "…";
}
