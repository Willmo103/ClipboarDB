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
using ClipboardHistoryApp.Forms;
using ClipboarDB.Forms;

namespace ClipboardHistoryApp
{
    class Program
    {
        internal static readonly string BaseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".macros", "ClipboardDB");
        internal static readonly string ConnectionString = $"Data Source={Path.Combine(BaseDirectory, "clipboard.db")};Version=3;";
        internal static readonly string ImagesDirectory = Path.Combine(BaseDirectory, "CopiedImages");
        internal static readonly string ConfigFile = Path.Combine(BaseDirectory, "config.json");

        internal static IConfiguration _configuration;
        internal static IntPtr _windowHandle;
        internal static ILogger<Program> _logger;
        internal static HistoryForm historyForm;
        internal const int WM_CLIPBOARDUPDATE = 0x031D;

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
                trayIcon.DoubleClick += (sender, e) => ViewHistoryMenuItem_Click(sender, e);

                ContextMenuStrip contextMenu = new ContextMenuStrip();
                ToolStripMenuItem viewHistoryMenuItem = new ToolStripMenuItem("View History");
                viewHistoryMenuItem.Click += ViewHistoryMenuItem_Click;
                contextMenu.Items.Add(viewHistoryMenuItem);
                ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit");
                exitMenuItem.Click += ExitMenuItem_Click;
                contextMenu.Items.Add(exitMenuItem);
                trayIcon.ContextMenuStrip = contextMenu;

                using (var listenerForm = new ClipboardListenerForm())
                {
                    listenerForm.Show();
                    _windowHandle = listenerForm.Handle;
                    AddClipboardFormatListener(_windowHandle);

                    Application.ApplicationExit += (sender, e) => RemoveClipboardFormatListener(_windowHandle);

                    Application.Run();
                }
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

        public static void OnClipboardUpdate()
        {
            IDataObject data = Clipboard.GetDataObject();
            if (data != null)
            {
                if (data.GetDataPresent(DataFormats.Text))
                {
                    string text = (string)data.GetData(DataFormats.Text);
                    SaveClipboardText(text);
                    // Notify the HistoryForm to refresh the history
                    historyForm?.RefreshHistory();
                }
                else if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    Bitmap bitmap = (Bitmap)data.GetData(DataFormats.Bitmap);
                    SaveClipboardImage(bitmap);
                    // Notify the HistoryForm to refresh the history
                    historyForm?.RefreshHistory();
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
            if (historyForm == null || historyForm.IsDisposed)
            {
                historyForm = new HistoryForm();
            }
            historyForm.ShowDialog();
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
                            historyForm?.RefreshHistory();
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
                            historyForm?.RefreshHistory();
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
}
