namespace WorkflowCore.WpfDemo;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                Environment.SetEnvironmentVariable("windir", systemRoot);
            }
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
