using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GrokoEngine.ImGuiEditor;

internal static class Program
{
    private static LoadingSplash? splash;

    [STAThread]
    private static void Main(string[] args)
    {
        // ── Game Mode ──
        // Si hay un game.json junto al ejecutable, este binario ES un juego exportado:
        // arranca a pantalla completa en Play Mode, sin UI de editor ni splash.
        var gameConfig = GameLaunchConfig.TryLoadBesideExecutable();
        if (gameConfig != null)
        {
            string gameProjectPath = AppContext.BaseDirectory; // los Assets/ van junto al exe
            try
            {
                using var game = new ImGuiEditorApp(gameProjectPath, gameConfig);
                game.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error al iniciar el juego: " + ex);
            }
            return;
        }

        string projectPath = args.Length > 0
            ? args[0]
            : ResolveLastProject();

        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            Console.Error.WriteLine("Uso: GrokoEngine.ImGuiEditor <ruta-del-proyecto>");
            return;
        }

        SaveLastProject(projectPath);
        ShowSplash(projectPath);
        try
        {
            using var editor = new ImGuiEditorApp(projectPath);
            editor.Run();
        }
        finally
        {
            CloseSplash();
        }
    }

    internal static void UpdateSplash(string detail, float progress)
    {
        splash?.Update(detail, progress);
    }

    internal static void CloseSplash()
    {
        splash?.Close();
        splash = null;
    }

    private static void ShowSplash(string projectPath)
    {
        splash = LoadingSplash.Show(Path.GetFileName(projectPath));
        UpdateSplash("Opening project", 0.02f);
    }

    private static string ResolveLastProject()
    {
        string lastProject = Path.Combine(AppContext.BaseDirectory, ".last_project");
        if (File.Exists(lastProject))
        {
            string saved = File.ReadAllText(lastProject).Trim();
            if (Directory.Exists(saved))
                return saved;
        }

        string[] candidates =
        {
            Path.Combine(Environment.CurrentDirectory, "GrokoEngine"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "GrokoEngine"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GrokoEngine")
        };

        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (Directory.Exists(Path.Combine(fullPath, "Assets")))
                return fullPath;
        }

        return "";
    }

    private static void SaveLastProject(string projectPath)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, ".last_project"), Path.GetFullPath(projectPath));
        }
        catch
        {
        }
    }

    private sealed class LoadingSplash : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEventSlim ready = new(false);
        private SplashForm? form;

        private LoadingSplash(string projectName)
        {
            thread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                form = new SplashForm(projectName);
                ready.Set();
                Application.Run(form);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            ready.Wait(2500);
        }

        public static LoadingSplash Show(string projectName) => new(projectName);

        public void Update(string detail, float progress)
        {
            var target = form;
            if (target == null || target.IsDisposed)
                return;

            void Apply() => target.SetProgress(detail, progress);
            try
            {
                if (target.InvokeRequired)
                    target.BeginInvoke((Action)Apply);
                else
                    Apply();
            }
            catch
            {
            }
        }

        public void Close()
        {
            var target = form;
            if (target != null && !target.IsDisposed)
            {
                try
                {
                    if (target.InvokeRequired)
                        target.BeginInvoke((Action)(() => target.Close()));
                    else
                        target.Close();
                }
                catch
                {
                }
            }

            if (thread.IsAlive)
                thread.Join(1200);
            ready.Dispose();
        }

        public void Dispose() => Close();
    }

    private sealed class SplashForm : Form
    {
        private readonly Label detailLabel;
        private readonly ProgressBar progressBar;

        public SplashForm(string projectName)
        {
            Text = "GrokoEngine";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(574, 406);
            BackColor = Color.FromArgb(9, 13, 18);
            ShowInTaskbar = false;
            TopMost = true;

            var title = new Label
            {
                Text = "GrokoEngine",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 34f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(38, 220)
            };
            Controls.Add(title);

            var version = new Label
            {
                Text = "Loading project",
                ForeColor = Color.FromArgb(210, 218, 230),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(44, 292)
            };
            Controls.Add(version);

            var project = new Label
            {
                Text = projectName,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(44, 316)
            };
            Controls.Add(project);

            detailLabel = new Label
            {
                Text = "Opening project",
                ForeColor = Color.FromArgb(220, 226, 236),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoEllipsis = true,
                Location = new Point(44, 338),
                Size = new Size(486, 18)
            };
            Controls.Add(detailLabel);

            progressBar = new ProgressBar
            {
                Location = new Point(44, 368),
                Size = new Size(486, 14),
                Minimum = 0,
                Maximum = 1000,
                Value = 20
            };
            Controls.Add(progressBar);

            Paint += (_, e) => PaintBackground(e.Graphics);
        }

        public void SetProgress(string detail, float progress)
        {
            detailLabel.Text = detail;
            progressBar.Value = Math.Clamp((int)(progress * 1000f), progressBar.Minimum, progressBar.Maximum);
            Invalidate();
        }

        private void PaintBackground(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 22, 31), Color.FromArgb(3, 7, 12), 45f);
            g.FillRectangle(bg, ClientRectangle);

            using var cyan = new SolidBrush(Color.FromArgb(170, 0, 150, 255));
            using var white = new SolidBrush(Color.FromArgb(210, 235, 245, 255));
            using var dark = new SolidBrush(Color.FromArgb(90, 18, 28, 42));
            var rnd = new Random(42);
            for (int i = 0; i < 72; i++)
            {
                int x = rnd.Next(-40, ClientSize.Width);
                int y = rnd.Next(20, 205);
                int w = rnd.Next(18, 78);
                int h = rnd.Next(4, 13);
                var brush = i % 9 == 0 ? cyan : i % 5 == 0 ? white : dark;
                g.FillRectangle(brush, x, y, w, h);
            }

            using var border = new Pen(Color.FromArgb(90, 80, 160, 255), 1f);
            g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }
}
