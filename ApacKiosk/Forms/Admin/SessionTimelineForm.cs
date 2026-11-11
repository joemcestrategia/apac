using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class SessionTimelineForm : Form
    {
        private readonly DatabaseManager _db;
        private readonly int _userId;
        private readonly string _userName;
        private readonly DateTime _from;
        private readonly DateTime _to;

        private FlowLayoutPanel _flowPanel;
        private Label _lblStatus;

        public SessionTimelineForm(DatabaseManager db, int userId, string userName, DateTime from, DateTime to)
        {
            _db = db;
            _userId = userId;
            _userName = userName;
            _from = from;
            _to = to;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = $"Linha do Tempo - {_userName} ({_from:dd/MM/yyyy} a {_to:dd/MM/yyyy})";
            Size = new Size(1024, 768);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(15, 15, 35);
            ForeColor = Color.White;
            ShowIcon = false;
            
            var topPanel = new Panel { Height = 60, Dock = DockStyle.Top, BackColor = Color.FromArgb(22, 22, 45) };
            
            var title = new Label
            {
                Text = $"🎬 Linha do Tempo da Sessão: {_userName}",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(124, 58, 237),
                Location = new Point(20, 15),
                AutoSize = true
            };
            
            var subtitle = new Label
            {
                Text = $"Mostrando eventos entre {_from:dd/MM/yyyy HH:mm} e {_to:dd/MM/yyyy HH:mm}",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(500, 22),
                AutoSize = true
            };

            topPanel.Controls.Add(title);
            topPanel.Controls.Add(subtitle);

            _flowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(20),
                BackColor = Color.FromArgb(10, 10, 25)
            };
            
            // Fixes scroll behavior for FlowLayoutPanel
            _flowPanel.HorizontalScroll.Maximum = 0;
            _flowPanel.AutoScroll = false;
            _flowPanel.VerticalScroll.Visible = false;
            _flowPanel.AutoScroll = true;

            _lblStatus = new Label
            {
                Text = "Carregando linha do tempo...",
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.White,
                AutoSize = true,
                Margin = new Padding(20)
            };
            _flowPanel.Controls.Add(_lblStatus);

            Controls.Add(_flowPanel);
            Controls.Add(topPanel);

            // Resize flow items when form is resized
            this.Resize += (s, e) => 
            {
                foreach (Control c in _flowPanel.Controls)
                {
                    if (c is Panel p)
                    {
                        p.Width = _flowPanel.ClientSize.Width - 50;
                    }
                }
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Application.DoEvents();
            LoadTimeline();
        }

        private void LoadTimeline()
        {
            _flowPanel.SuspendLayout();
            _flowPanel.Controls.Clear();

            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT timestamp, type, description, file_path 
                                    FROM log_entries 
                                    WHERE user_id = @uid 
                                      AND timestamp >= @from 
                                      AND timestamp <= @to 
                                    ORDER BY timestamp ASC 
                                    LIMIT 2000";

                cmd.Parameters.AddWithValue("@uid", _userId);
                cmd.Parameters.AddWithValue("@from", _from);
                cmd.Parameters.AddWithValue("@to", _to.AddDays(1)); // end of day

                using var reader = cmd.ExecuteReader();
                int count = 0;

                while (reader.Read())
                {
                    var timestamp = reader.GetDateTime(0);
                    var type = reader.GetString(1);
                    var desc = reader.IsDBNull(2) ? "" : reader.GetString(2);
                    var filePath = reader.IsDBNull(3) ? "" : reader.GetString(3);

                    var card = CreateEventCard(timestamp, type, desc, filePath);
                    _flowPanel.Controls.Add(card);
                    count++;
                }

                if (count == 0)
                {
                    _flowPanel.Controls.Add(new Label { Text = "Nenhum evento encontrado nesse período.", Font = new Font("Segoe UI", 12), ForeColor = Color.Gray, AutoSize = true });
                }
                else if (count == 2000)
                {
                    _flowPanel.Controls.Add(new Label { Text = "⚠️ Limite de 2000 eventos atingido. Reduza o filtro de datas para ver mais detalhes.", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(245, 158, 11), AutoSize = true, Margin = new Padding(10) });
                }
            }
            catch (Exception ex)
            {
                KioskDialog.ShowMessage("Erro ao carregar linha do tempo: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error, this);
            }
            finally
            {
                _flowPanel.ResumeLayout();
            }
        }

        private Panel CreateEventCard(DateTime timestamp, string type, string desc, string filePath)
        {
            int cardWidth = _flowPanel.ClientSize.Width - 50;
            var card = new Panel
            {
                Width = cardWidth,
                AutoSize = true,
                BackColor = Color.FromArgb(26, 26, 46),
                Margin = new Padding(0, 0, 0, 15),
                Padding = new Padding(15)
            };

            // Timestamp label
            var lblTime = new Label
            {
                Text = timestamp.ToString("dd/MM/yyyy HH:mm:ss"),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(15, 15),
                AutoSize = true
            };
            card.Controls.Add(lblTime);

            // Icon and title
            string icon = "📌";
            Color titleColor = Color.White;
            
            if (type == "screenshot") { icon = "📸"; titleColor = Color.LightSkyBlue; }
            else if (type == "camera") { icon = "📷"; titleColor = Color.LightCoral; }
            else if (type == "keylog") { icon = "⌨️"; titleColor = Color.LightGreen; }
            else if (type == "navigation_allowed") { icon = "✅"; titleColor = Color.MediumSeaGreen; }
            else if (type == "navigation_blocked" || type == "blocked_site") { icon = "🚫"; titleColor = Color.OrangeRed; }

            var lblTitle = new Label
            {
                Text = $"{icon} {type.ToUpper()}",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = titleColor,
                Location = new Point(200, 15),
                AutoSize = true
            };
            card.Controls.Add(lblTitle);

            int currentY = 40;

            // Description
            if (!string.IsNullOrEmpty(desc))
            {
                var lblDesc = new Label
                {
                    Text = desc,
                    Font = new Font("Segoe UI", 10),
                    ForeColor = Color.FromArgb(220, 220, 230),
                    Location = new Point(15, currentY),
                    Width = cardWidth - 30,
                    AutoSize = true
                };
                card.Controls.Add(lblDesc);
                currentY += lblDesc.Height + 10;
            }

            // File Content (Image or Text)
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                if (type == "screenshot" || type == "camera")
                {
                    try
                    {
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            var img = Image.FromStream(fs);
                            int imgHeight = (int)((double)img.Height / img.Width * (cardWidth - 30));
                            if (imgHeight > 600) imgHeight = 600; // max height cap

                            var pic = new PictureBox
                            {
                                Image = new Bitmap(img), // clone to avoid locking stream
                                Location = new Point(15, currentY),
                                Size = new Size(cardWidth - 30, imgHeight),
                                SizeMode = PictureBoxSizeMode.Zoom,
                                BackColor = Color.Black
                            };
                            card.Controls.Add(pic);
                            currentY += pic.Height + 10;
                        }
                    }
                    catch { }
                }
                else if (type == "keylog")
                {
                    try
                    {
                        var content = File.ReadAllText(filePath);
                        var txt = new TextBox
                        {
                            Text = content,
                            Multiline = true,
                            ReadOnly = true,
                            Location = new Point(15, currentY),
                            Width = cardWidth - 30,
                            Height = Math.Min(300, 50 + (content.Split('\n').Length * 15)),
                            ScrollBars = ScrollBars.Vertical,
                            Font = new Font("Consolas", 10),
                            BackColor = Color.FromArgb(15, 15, 35),
                            ForeColor = Color.FromArgb(167, 139, 250),
                            BorderStyle = BorderStyle.FixedSingle
                        };
                        card.Controls.Add(txt);
                        currentY += txt.Height + 10;
                    }
                    catch { }
                }
            }

            card.Height = currentY + 10;
            return card;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Clean up images memory
            foreach (Control c in _flowPanel.Controls)
            {
                foreach (Control child in c.Controls)
                {
                    if (child is PictureBox pic && pic.Image != null)
                    {
                        pic.Image.Dispose();
                    }
                }
            }
            base.OnFormClosed(e);
        }
    }
}
