using System;
using System.Drawing;
using System.Windows.Forms;

namespace RageLightEditor.Editor
{
    public sealed class GameViewForm_U16 : Form
    {
        public event Action AttachRequested;
        public event Action PlacementChanged;

        private readonly Label debug = new Label();
        private readonly Button attach = new Button();

        public GameViewForm_U16()
        {
            Text = "Game view";
            BackColor = Color.FromArgb(10, 10, 12);
            ForeColor = Color.Gainsboro;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            MinimumSize = new Size(320, 220);
            Size = new Size(960, 620);
            FormBorderStyle = FormBorderStyle.Sizable;
            attach.Text = "Attach back to the tool";
            attach.Dock = DockStyle.Top;
            attach.Height = 26;
            attach.FlatStyle = FlatStyle.Flat;
            attach.ForeColor = Color.Gainsboro;
            attach.BackColor = Color.FromArgb(28, 28, 34);
            attach.Click += (s, e) => AttachRequested?.Invoke();
            debug.Dock = DockStyle.Bottom;
            debug.Height = 88;
            debug.Font = new Font("Consolas", 9.0f);
            debug.Padding = new Padding(8, 4, 8, 4);
            debug.AutoSize = false;
            debug.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(debug);
            Controls.Add(attach);
            Move += (s, e) => PlacementChanged?.Invoke();
            Resize += (s, e) => PlacementChanged?.Invoke();
            FormClosing += (s, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                AttachRequested?.Invoke();
            };
        }

        public bool ShowDebug
        {
            get => debug.Visible;
            set => debug.Visible = value;
        }

        public void SetDebug(string text)
        {
            if (debug.Text != text) debug.Text = text;
        }

        public Rectangle PictureArea()
        {
            var r = ClientRectangle;
            int top = attach.Visible ? attach.Height : 0;
            int bottom = debug.Visible ? debug.Height : 0;
            return new Rectangle(0, top, Math.Max(r.Width, 1), Math.Max(r.Height - top - bottom, 1));
        }
    }
}
