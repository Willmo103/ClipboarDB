using System;
using System.Data.SQLite;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace ClipboardHistoryApp.Forms
{
	public class HistoryForm : DarkForm
	{
		public FlowLayoutPanel panel;

		public HistoryForm()
		{
			Width = 800;
			Height = 600;
			BackColor = Color.FromArgb(60, 60, 60);
			StartPosition = FormStartPosition.WindowsDefaultLocation;
			MinimumSize = new Size(400, 300);
			AutoScroll = true;
			ClientSize = new Size(800, 600);

			panel = new FlowLayoutPanel
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
			panel.Controls.Clear(); // Clear existing controls to refresh the panel

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
							Width = panel.ClientSize.Width, // Adjust width to fit within the panel
							Height = 120,
							Padding = new Padding(15),
							BackColor = Color.FromArgb(50, 50, 50),
							Margin = new Padding(0, 0, 0, 10)
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
						else
						{
							TextBox contentTextBox = new TextBox
							{
								Text = reader["Content"].ToString(),
								Width = itemPanel.Width,
								Height = 80,
								Multiline = true,
								ReadOnly = true,
								ForeColor = Color.White,
								BackColor = Color.FromArgb(50, 50, 50), // Set a solid background color
								BorderStyle = BorderStyle.None,
								Padding = new Padding(10),
								Font = new Font("Consolas", 10),
								ScrollBars = ScrollBars.Vertical
							};
							itemPanel.Controls.Add(contentTextBox);
						}
						//Button deleteButton = new Button
						//{
						//	Text = "Delete",
						//	Width = 30,
						//	Height = 10,
						//	FlatStyle = FlatStyle.Flat,
						//	ForeColor = Color.Red,
						//	BackColor = Color.FromArgb(70, 70, 70)
						//};
						//deleteButton.Click += (s, e) =>
						//{
						//	using (SQLiteCommand deleteCommand = new SQLiteCommand("DELETE FROM ClipboardHistory WHERE Id = @Id", connection))
						//	{
						//		deleteCommand.Parameters.AddWithValue("@Id", reader["Id"]);
						//		deleteCommand.ExecuteNonQuery();
						//		panel.Controls.Remove(itemPanel);
						//	}
						//};

						Label timestampLabel = new()
						{
							Text = reader["Timestamp"].ToString(),
							Dock = DockStyle.Bottom,
							TextAlign = ContentAlignment.BottomRight,
							ForeColor = Color.White,
							BackColor = Color.Transparent
						};

						//itemPanel.Controls.Add(deleteButton);
						itemPanel.Controls.Add(timestampLabel);

						panel.Controls.Add(itemPanel);
						panel.Controls.Add(new Panel { Height = 2, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(80, 80, 80) });
					}
				}
			}
		}

		public void RefreshHistory()
		{
			LoadHistory(this.panel); // Call LoadHistory to refresh the panel
		}
	}
}
