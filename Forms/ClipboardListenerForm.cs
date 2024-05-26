using System;
using System.Windows.Forms;
using ClipboardHistoryApp;

namespace ClipboarDB.Forms
{
    public class ClipboardListenerForm : Form
    {
        public ClipboardListenerForm()
        {
            // Set up the form to be hidden
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Load += (sender, e) => Hide();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_CLIPBOARDUPDATE)
            {
                Program.OnClipboardUpdate();
            }
            base.WndProc(ref m);
        }
    }
}
