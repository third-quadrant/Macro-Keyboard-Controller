using System.IO;

namespace Fireworm.App;

public sealed record InstalledProgram(string Name, string Path)
{
    public override string ToString() => Name;
}

/// <summary>Start-menu shortcuts are the closest thing Windows has to an installed-program list.</summary>
public static class InstalledPrograms
{
    private static readonly string[] Noise =
    {
        "uninstall", "제거", "readme", "help", "도움말", "manual", "설명서", "website", "웹 사이트",
        "documentation", "license", "changelog",
    };

    public static List<InstalledProgram> Scan()
    {
        var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        var shell = CreateShell();
        if (shell == null) return new List<InstalledProgram>();

        foreach (var root in Roots())
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> shortcuts;
            try { shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
            catch (Exception) { continue; }

            foreach (var shortcut in shortcuts)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(shortcut);
                if (Noise.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

                var target = ResolveTarget(shell, shortcut);
                if (string.IsNullOrEmpty(target) || !File.Exists(target)) continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

                var display = name.Length > 60 ? name[..60] : name;
                found.TryAdd(target, new InstalledProgram(display, target));
            }
        }

        return found.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<string> Roots()
    {
        yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    }

    private static object? CreateShell()
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            return type == null ? null : Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveTarget(object shell, string shortcutPath)
    {
        try
        {
            dynamic link = ((dynamic)shell).CreateShortcut(shortcutPath);
            return (string?)link.TargetPath;
        }
        catch
        {
            return null;
        }
    }
}
