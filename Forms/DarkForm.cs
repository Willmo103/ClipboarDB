using System.Drawing;
using System.Windows.Forms;


namespace ClipboardHistoryApp.Forms
{
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
}