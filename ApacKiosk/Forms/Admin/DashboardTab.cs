using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class DashboardTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private Label _activeUsersLabel;
        private Label _todayUsageLabel;
        private Label _totalUsersLabel;
        private Label _totalProfilesLabel;
        private DataGridView _recentLogsGrid;
        private DataGridView _recentSessionsGrid;

        public DashboardTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var cardsPanel = new FlowLayoutPanel
            {
                Location = new Point(0, 0),
                Size = new Size(1000, 130),
                BackColor = Color.Transparent,
                AutoSize = false
            };

            _activeUsersLabel = CreateCard(cardsPanel, "Usuários Ativos Agora", "0", Color.FromArgb(124, 58, 237));
            _todayUsageLabel = CreateCard(cardsPanel, "Tempo de Uso Hoje", "0h 0min", Color.FromArgb(16, 185, 129));
            _totalUsersLabel = CreateCard(cardsPanel, "Total de Usuários", "0", Color.FromArgb(59, 130, 246));
            _totalProfilesLabel = CreateCard(cardsPanel, "Perfis de Acesso", "0", Color.FromArgb(245, 158, 11));

            var sessionsLabel = new Label
            {
                Text = "Sessões Recentes",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(0, 140),
                AutoSize = true
            };

            _recentSessionsGrid = new DataGridView
            {
                Location = new Point(0, 170),
                Size = new Size(1000, 170),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                EnableHeadersVisualStyles = false
            };
            _recentSessionsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _recentSessionsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _recentSessionsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _recentSessionsGrid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _recentSessionsGrid.DefaultCellStyle.ForeColor = Color.White;
            _recentSessionsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);

            _recentSessionsGrid.Columns.Add("User", "Usuário");
            _recentSessionsGrid.Columns.Add("Login", "Login");
            _recentSessionsGrid.Columns.Add("Logout", "Logout");
            _recentSessionsGrid.Columns.Add("Duration", "Duração");

            var logsLabel = new Label
            {
                Text = "Últimos Eventos",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(0, 350),
                AutoSize = true
            };

            _recentLogsGrid = new DataGridView
            {
                Location = new Point(0, 380),
                Size = new Size(1000, 280),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                EnableHeadersVisualStyles = false
            };
            _recentLogsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _recentLogsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _recentLogsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _recentLogsGrid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _recentLogsGrid.DefaultCellStyle.ForeColor = Color.White;
            _recentLogsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);

            _recentLogsGrid.Columns.Add("Timestamp", "Data/Hora");
            _recentLogsGrid.Columns.Add("User", "Usuário");
            _recentLogsGrid.Columns.Add("Type", "Tipo");
            _recentLogsGrid.Columns.Add("Description", "Descrição");

            Controls.AddRange(new Control[] { cardsPanel, sessionsLabel, _recentSessionsGrid, logsLabel, _recentLogsGrid });

            var refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            refreshTimer.Tick += (s, e) => RefreshData();
            refreshTimer.Start();
            RefreshData();
        }

        private Label CreateCard(FlowLayoutPanel panel, string title, string value, Color accent)
        {
            var card = new Panel
            {
                Size = new Size(230, 110),
                BackColor = Color.FromArgb(26, 26, 46),
                Margin = new Padding(8),
                Padding = new Padding(12)
            };

            var titleLbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 11),
                ForeColor = Color.FromArgb(180, 180, 200),
                Location = new Point(12, 12),
                AutoSize = true
            };

            var valueLbl = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = accent,
                Location = new Point(12, 42),
                AutoSize = true,
                Tag = accent
            };

            card.Controls.AddRange(new Control[] { titleLbl, valueLbl });
            panel.Controls.Add(card);
            return valueLbl;
        }

        private void RefreshData()
        {
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT COUNT(*) FROM session_logs WHERE logout_time IS NULL";
                _activeUsersLabel.Text = cmd.ExecuteScalar()?.ToString() ?? "0";

                cmd.CommandText = "SELECT COALESCE(SUM(duration_seconds), 0) FROM session_logs WHERE date(login_time) = date('now')";
                var totalSec = (long)cmd.ExecuteScalar()!;
                _todayUsageLabel.Text = $"{totalSec / 3600}h {(totalSec % 3600) / 60}min";

                cmd.CommandText = "SELECT COUNT(*) FROM users";
                _totalUsersLabel.Text = cmd.ExecuteScalar()?.ToString() ?? "0";

                cmd.CommandText = "SELECT COUNT(*) FROM access_profiles";
                _totalProfilesLabel.Text = cmd.ExecuteScalar()?.ToString() ?? "0";

                _recentSessionsGrid.Rows.Clear();
                cmd.CommandText = @"SELECT u.full_name, s.login_time, s.logout_time, s.duration_seconds
                                   FROM session_logs s JOIN users u ON s.user_id = u.id
                                   ORDER BY s.login_time DESC LIMIT 20";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var dur = reader.GetInt32(3);
                        _recentSessionsGrid.Rows.Add(
                            reader.GetString(0),
                            reader.GetDateTime(1).ToString("dd/MM/yyyy HH:mm"),
                            reader.IsDBNull(2) ? "Ativa" : reader.GetDateTime(2).ToString("dd/MM/yyyy HH:mm"),
                            $"{dur / 3600}h {(dur % 3600) / 60}m {dur % 60}s"
                        );
                    }
                }

                _recentLogsGrid.Rows.Clear();
                cmd.CommandText = @"SELECT l.timestamp, COALESCE(u.full_name, 'Sistema'), l.type, COALESCE(l.description, '')
                                   FROM log_entries l LEFT JOIN users u ON l.user_id = u.id
                                   ORDER BY l.timestamp DESC LIMIT 50";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _recentLogsGrid.Rows.Add(
                            reader.GetDateTime(0).ToString("dd/MM/yyyy HH:mm:ss"),
                            reader.GetString(1),
                            reader.GetString(2),
                            reader.GetString(3)
                        );
                    }
                }
            }
            catch { }
        }
    }
}
