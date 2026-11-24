namespace InputSink;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new Form
        {
            Text = "Fireworm Input Sink",
            Width = 560,
            Height = 220,
            StartPosition = FormStartPosition.CenterScreen,
        };
        var box = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Name = "InputBox",
            AccessibleName = "InputBox",
            Font = new Font("Consolas", 14f),
        };
        form.Controls.Add(box);
        form.ActiveControl = box;
        form.Shown += (_, _) => box.Focus();
        Application.Run(form);
    }
}
