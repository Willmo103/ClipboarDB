using System;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Microsoft.Extensions.Configuration.Json;

namespace ClipboardHistoryApp
{
    class Program
    {
        private static readonly string BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".macros", "ClipboardDB");
        internal static readonly string ConnectionString = $"Data Source={Path.Combine(BaseDirectory, "clipboard.db")};Version=3;";
        internal static readonly string ImagesDirectory = Path.Combine(BaseDirectory, "CopiedImages");
        private static readonly string ConfigFile = Path.Combine(BaseDirectory, "config.json");

        private static IConfiguration _configuration;
        internal static IntPtr _windowHandle;
        internal static ILogger<Program> _logger;

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [STAThread]
        static void Main()
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(new CompactJsonFormatter(), Path.Combine(BaseDirectory, "logs", "log.json"), rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSerilog(dispose: true);
            });

            _logger = loggerFactory.CreateLogger<Program>();

            try
            {
                TryInit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during initialization.");
                throw;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (NotifyIcon trayIcon = new NotifyIcon())
            {
                // Use a default system icon
                trayIcon.Icon = SystemIcons.Information;
                trayIcon.Text = "Clipboard History";
                trayIcon.Visible = true;

                ContextMenuStrip contextMenu = new ContextMenuStrip();
                ToolStripMenuItem viewHistoryMenuItem = new ToolStripMenuItem("View History");
                viewHistoryMenuItem.Click += ViewHistoryMenuItem_Click;
                contextMenu.Items.Add(viewHistoryMenuItem);
                ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit");
                exitMenuItem.Click += ExitMenuItem_Click;
                contextMenu.Items.Add(exitMenuItem);
                trayIcon.ContextMenuStrip = contextMenu;

                Application.ApplicationExit += (sender, e) => RemoveClipboardFormatListener(_windowHandle);

                Application.Run();
            }
        }

        private static void TryInit()
        {
            if (!Directory.Exists(BaseDirectory))
            {
                Directory.CreateDirectory(BaseDirectory);
            }

            if (!Directory.Exists(Path.Combine(BaseDirectory, "logs")))
            {
                Directory.CreateDirectory(Path.Combine(BaseDirectory, "logs"));
            }

            CreateDatabase();
            CreateImagesDirectory();
            LoadConfiguration();
        }

        private static void CreateDatabase()
        {
            if (!File.Exists(Path.Combine(BaseDirectory, "clipboard.db")))
            {
                SQLiteConnection.CreateFile(Path.Combine(BaseDirectory, "clipboard.db"));
                using (SQLiteConnection connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    string sql = "CREATE TABLE ClipboardHistory (Id INTEGER PRIMARY KEY AUTOINCREMENT, Content TEXT, ImagePath TEXT, Hash TEXT, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP)";
                    using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
            else
            {
                using (SQLiteConnection connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();
                    string sql = "ALTER TABLE ClipboardHistory ADD COLUMN Hash TEXT";
                    using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                    {
                        try
                        {
                            command.ExecuteNonQuery();
                        }
                        catch (SQLiteException)
                        {
                            // Ignore exception if column already exists
                        }
                    }
                }
            }
        }

        private static void CreateImagesDirectory()
        {
            Directory.CreateDirectory(ImagesDirectory);
        }

        private static void LoadConfiguration()
        {
            var _configPath = ConfigFile;
            if (!File.Exists(_configPath))
            {
                 _configuration = new ConfigurationBuilder()
                    .AddJsonFile(_configPath, optional: true, reloadOnChange: true)
                    .Build();
            }
        }

        private static void ViewHistoryMenuItem_Click(object sender, EventArgs e)
        {
            using (HistoryForm historyForm = new HistoryForm())
            {
                historyForm.ShowDialog();
            }
        }

        private static void ExitMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        public static string ComputeHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public static void HashAndCompare(string content, string imagePath = null)
        {
            string hash = ComputeHash(content);

            using (SQLiteConnection connection = new SQLiteConnection(ConnectionString))
            {
                connection.Open();
                string sql = "SELECT Id FROM ClipboardHistory WHERE Hash = @Hash";
                using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@Hash", hash);
                    var result = command.ExecuteScalar();

                    if (result != null)
                    {
                        int id = Convert.ToInt32(result);
                        sql = "UPDATE ClipboardHistory SET Timestamp = CURRENT_TIMESTAMP WHERE Id = @Id";
                        using (SQLiteCommand updateCommand = new SQLiteCommand(sql, connection))
                        {
                            updateCommand.Parameters.AddWithValue("@Id", id);
                            updateCommand.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        sql = "INSERT INTO ClipboardHistory (Content, ImagePath, Hash) VALUES (@Content, @ImagePath, @Hash)";
                        using (SQLiteCommand insertCommand = new SQLiteCommand(sql, connection))
                        {
                            insertCommand.Parameters.AddWithValue("@Content", content);
                            insertCommand.Parameters.AddWithValue("@ImagePath", imagePath);
                            insertCommand.Parameters.AddWithValue("@Hash", hash);
                            insertCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public static void SaveClipboardText(string text)
        {
            HashAndCompare(text);
            _logger.LogInformation("Text content copied at {time}", DateTime.Now);
        }

        public static void SaveClipboardImage(Bitmap bitmap)
        {
            string filePath = Path.Combine(ImagesDirectory, $"{Guid.NewGuid()}.png");
            bitmap.Save(filePath, ImageFormat.Png);
            HashAndCompare(filePath, filePath);
            _logger.LogInformation("Image content copied at {time}", DateTime.Now);
        }

        public static Image GetThumbnail(string imagePath, int width, int height)
        {
            using (Image image = Image.FromFile(imagePath))
            {
                return image.GetThumbnailImage(width, height, () => false, IntPtr.Zero);
            }
        }
    }

    public class ClipboardNotificationForm : Form
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        public ClipboardNotificationForm()
        {
            Program._windowHandle = this.Handle;
            Program.AddClipboardFormatListener(this.Handle);
            this.FormClosing += new FormClosingEventHandler(ClipboardNotificationForm_FormClosing);
            this.Shown += new EventHandler(ClipboardNotificationForm_Shown);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data != null)
                {
                    if (data.GetDataPresent(DataFormats.Text))
                    {
                        string text = (string)data.GetData(DataFormats.Text);
                        Program.SaveClipboardText(text);
                    }
                    else if (data.GetDataPresent(DataFormats.Bitmap))
                    {
                        Bitmap bitmap = (Bitmap)data.GetData(DataFormats.Bitmap);
                        Program.SaveClipboardImage(bitmap);
                    }
                }
            }
            base.WndProc(ref m);
        }

        private void ClipboardNotificationForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void ClipboardNotificationForm_Shown(object sender, EventArgs e)
        {
            this.Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Program.RemoveClipboardFormatListener(this.Handle);
            }
            base.Dispose(disposing);
        }
    }
    public class DarkForm : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Custom title bar color
            Color titleBarColor = Color.FromArgb(45, 45, 45);

            // Paint the title bar
            Rectangle titleBarRect = new Rectangle(0, 0, Width, 30);
            using (SolidBrush brush = new SolidBrush(titleBarColor))
            {
                e.Graphics.FillRectangle(brush, titleBarRect);
            }

            // Custom title text
            string title = Text;
            using (Font titleFont = new Font("Segoe UI", 10))
            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            {
                StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };
                e.Graphics.DrawString(title, titleFont, titleBrush, titleBarRect, sf);
            }
        }
    }

    public class HistoryForm : DarkForm
    {
        public HistoryForm()
        {
            Text = "Clipboard History";
            Width = 800;
            Height = 600;
            BackColor = Color.FromArgb(60, 60, 60);
            StartPosition = FormStartPosition.CenterScreen;

            FlowLayoutPanel panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(10)
            };

            LoadHistory(panel);

            Controls.Add(panel);
        }

        private void LoadHistory(FlowLayoutPanel panel)
        {
            using (SQLiteConnection connection = new SQLiteConnection(Program.ConnectionString))
            {
                connection.Open();
                string sql = "SELECT Id, Content, ImagePath, Timestamp FROM ClipboardHistory ORDER BY Timestamp DESC";
                using (SQLiteCommand command = new SQLiteCommand(sql, connection))
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Panel itemPanel = new Panel
                        {
                            Width = 760,
                            Height = 120,
                            Padding = new Padding(10),
                            BackColor = Color.FromArgb(70, 70, 70)
                        };

                        if (!string.IsNullOrEmpty(reader["ImagePath"].ToString()))
                        {
                            PictureBox pictureBox = new PictureBox
                            {
                                Image = Program.GetThumbnail(reader["ImagePath"].ToString(), 80, 80),
                                SizeMode = PictureBoxSizeMode.Zoom,
                                Width = 80,
                                Height = 80,
                                Cursor = Cursors.Hand
                            };
                            pictureBox.Click += (s, e) =>
                            {
                                try
                                {
                                    System.Diagnostics.Process.Start("explorer", reader["ImagePath"].ToString());
                                }
                                catch (Exception ex)
                                {
                                    Program._logger.LogError(ex, "An error occurred while opening the image.");
                                }
                            };
                            itemPanel.Controls.Add(pictureBox);
                        }

                        Label contentLabel = new Label
                        {
                            Text = reader["Content"].ToString(),
                            Width = 600,
                            Height = 80,
                            AutoSize = true,
                            ForeColor = Color.White,
                            BackColor = Color.Transparent,
                            Padding = new Padding(5),
                            Font = new Font("Consolas", 10)
                        };

                        Button deleteButton = new Button
                        {
                            Text = "Delete",
                            Width = 80,
                            Height = 30,
                            BackColor = SystemColors.ControlLightLight
                        };
                        deleteButton.Click += (s, e) =>
                        {
                            using (SQLiteCommand deleteCommand = new SQLiteCommand("DELETE FROM ClipboardHistory WHERE Id = @Id", connection))
                            {
                                deleteCommand.Parameters.AddWithValue("@Id", reader["Id"]);
                                deleteCommand.ExecuteNonQuery();
                                panel.Controls.Remove(itemPanel);
                            }
                        };

                        Label timestampLabel = new Label
                        {
                            Text = reader["Timestamp"].ToString(),
                            Dock = DockStyle.Bottom,
                            ForeColor = Color.White,
                            BackColor = Color.Transparent
                        };

                        itemPanel.Controls.Add(contentLabel);
                        itemPanel.Controls.Add(deleteButton);
                        itemPanel.Controls.Add(timestampLabel);

                        panel.Controls.Add(itemPanel);
                        panel.Controls.Add(new Panel { Height = 1, Dock = DockStyle.Bottom, BackColor = Color.Gray });
                    }
                }
            }
        }
    }
}
