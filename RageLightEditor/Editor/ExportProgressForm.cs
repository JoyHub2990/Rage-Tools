using System;
using System.Drawing;
using System.Windows.Forms;

namespace RageLightEditor.Editor
{
    public class ExportProgressForm : Form
    {
        private readonly ProgressBar bar;
        private readonly Label headline;
        private readonly Label detail;
        private readonly Button cancel;

        private readonly int total;
        private readonly double[] recent = new double[30];
        private int recentCount;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private double lastFrameAt;

        public bool Cancelled { get; private set; }

        public ExportProgressForm(string title, string what, int totalFrames)
        {
            total = Math.Max(1, totalFrames);

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 150);
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(32, 32, 34);
            ForeColor = Color.FromArgb(226, 226, 228);

            headline = new Label
            {
                Text = what,
                Location = new Point(16, 14),
                Size = new Size(428, 20),
                ForeColor = Color.FromArgb(226, 226, 228),
            };
            bar = new ProgressBar
            {
                Location = new Point(16, 42),
                Size = new Size(428, 22),
                Minimum = 0,
                Maximum = total,
                Style = ProgressBarStyle.Continuous,
            };
            detail = new Label
            {
                Text = "Starting...",
                Location = new Point(16, 72),
                Size = new Size(428, 34),
                ForeColor = Color.FromArgb(160, 160, 164),
            };
            cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(354, 110),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 56, 60),
                ForeColor = Color.FromArgb(226, 226, 228),
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 84);
            cancel.Click += (s, e) =>
            {
                Cancelled = true;
                cancel.Enabled = false;
                cancel.Text = "Stopping...";
                detail.Text = "Finishing the frame in progress, then closing the file.";
            };

            Controls.Add(headline);
            Controls.Add(bar);
            Controls.Add(detail);
            Controls.Add(cancel);

            FormClosing += (s, e) =>
            {
                if (!Cancelled && e.CloseReason == CloseReason.UserClosing)
                {
                    Cancelled = true;
                    e.Cancel = true;
                }
            };
        }

        public void SetMessage(string what)
        {
            if (bar.Style != ProgressBarStyle.Marquee)
            {
                bar.Style = ProgressBarStyle.Marquee;
                bar.MarqueeAnimationSpeed = 30;
            }
            detail.Text = what ?? "";
            Application.DoEvents();
        }

        public void Advance(int frameIndex)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (lastFrameAt > 0)
            {
                recent[recentCount % recent.Length] = now - lastFrameAt;
                recentCount++;
            }
            lastFrameAt = now;

            bar.Value = Math.Min(frameIndex + 1, total);

            string eta = "estimating...";
            int n = Math.Min(recentCount, recent.Length);
            if (n >= 3)
            {
                double sum = 0;
                for (int i = 0; i < n; i++) sum += recent[i];
                double per = sum / n;
                double left = per * (total - frameIndex - 1);
                eta = left < 1 ? "almost done" : $"about {Describe(left)} left";
                detail.Text = $"Frame {frameIndex + 1} of {total}   -   {eta}\n" +
                              $"{per * 1000.0:0} ms a frame, {Describe(now)} elapsed";
            }
            else
            {
                detail.Text = $"Frame {frameIndex + 1} of {total}   -   {eta}";
            }

            Application.DoEvents();
        }

        private static string Describe(double seconds)
        {
            if (seconds < 60) return $"{seconds:0} sec";
            if (seconds < 3600) return $"{seconds / 60.0:0} min {seconds % 60:0} sec";
            return $"{seconds / 3600.0:0} hr {(seconds % 3600) / 60.0:0} min";
        }
    }
}

