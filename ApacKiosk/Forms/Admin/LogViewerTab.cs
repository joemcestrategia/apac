using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class LogViewerTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private DataGridView _grid;
        private ComboBox _cmbUser;
        private ComboBox _cmbType;
        private DateTimePicker _dtpFrom;
        private DateTimePicker _dtpTo;
        private Button _btnSearch;
        private Button _btnExport;
        private Button _btnCleanup;
        private Label _lblPreviewTitle;
        private PictureBox _previewBox;
        private TextBox _previewText;
        private TabControl _previewTabs;
        private TextBox _txtKeylogContent;

        public LogViewerTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
            LoadFilters();
            Search();
        }

        private void InitializeComponent()
        {
            var topPanel = new Panel { Height = 80, Dock = DockStyle.Top, BackColor = Color.FromArgb(22, 22, 45) };

            var lblUser = new Label { Text = "Usuário:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(10, 12), AutoSize = true };
            _cmbUser = new ComboBox { Location = new Point(10, 32), Size = new Size(170, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White };
            _cmbUser.Items.Add(new ComboItem<int?>(null, "Todos"));

            var lblType = new Label { Text = "Tipo:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(195, 12), AutoSize = true };
            _cmbType = new ComboBox { Location = new Point(195, 32), Size = new Size(150, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White };
            _cmbType.Items.AddRange(new[] { "Todos", "screenshot", "camera", "keylog", "event", "navigation_allowed", "navigation_blocked", "blocked_site", "admin_login" });

            var lblFrom = new Label { Text = "De:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(360, 12), AutoSize = true };
            _dtpFrom = new DateTimePicker { Location = new Point(360, 32), Size = new Size(160, 28), Format = DateTimePickerFormat.Short };

            var lblTo = new Label { Text = "Até:", Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(530, 12), AutoSize = true };
            _dtpTo = new DateTimePicker { Location = new Point(530, 32), Size = new Size(160, 28), Format = DateTimePickerFormat.Short };

            _btnSearch = new Button { Text = "🔍 Buscar", Location = new Point(705, 28), Size = new Size(100, 35), Font = new Font("Segoe UI", 10, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand };
            _btnSearch.FlatAppearance.BorderSize = 0;
            _btnSearch.Click += (s, e) => Search();

            _btnExport = new Button { Text = "📦 Exportar", Location = new Point(815, 28), Size = new Size(100, 35), Font = new Font("Segoe UI", 10, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White, Cursor = Cursors.Hand };
            _btnExport.FlatAppearance.BorderSize = 0;
            _btnExport.Click += (s, e) => ExportLogs();

            _btnCleanup = new Button { Text = "🧹 Limpar", Location = new Point(925, 28), Size = new Size(90, 35), Font = new Font("Segoe UI", 9, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White, Cursor = Cursors.Hand };
            _btnCleanup.FlatAppearance.BorderSize = 0;
            _btnCleanup.Click += (s, e) => CleanupLogs();

            topPanel.Controls.AddRange(new Control[] { lblUser, _cmbUser, lblType, _cmbType, lblFrom, _dtpFrom, lblTo, _dtpTo, _btnSearch, _btnExport, _btnCleanup });

            _grid = new DataGridView
            {
                Location = new Point(0, 90),
                Size = new Size(1020, 250),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                EnableHeadersVisualStyles = false
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            _grid.Columns.Add("Timestamp", "Data/Hora");
            _grid.Columns.Add("User", "Usuário");
            _grid.Columns.Add("Type", "Tipo");
            _grid.Columns.Add("Description", "Descrição");
            _grid.Columns.Add("FilePath", "Arquivo");
            _grid.SelectionChanged += (s, e) => ShowPreview();

            _previewTabs = new TabControl
            {
                Location = new Point(0, 350),
                Size = new Size(1020, 330),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(22, 22, 45)
            };

            var tabImage = new TabPage("Imagem") { BackColor = Color.FromArgb(22, 22, 45) };
            _previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(15, 15, 35)
            };
            tabImage.Controls.Add(_previewBox);

            var tabKeylog = new TabPage("Keylog") { BackColor = Color.FromArgb(22, 22, 45) };
            _txtKeylogContent = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 10),
                BackColor = Color.FromArgb(15, 15, 35),
                ForeColor = Color.FromArgb(167, 139, 250),
                BorderStyle = BorderStyle.None
            };
            tabKeylog.Controls.Add(_txtKeylogContent);

            var tabText = new TabPage("Detalhes") { BackColor = Color.FromArgb(22, 22, 45) };
            _previewText = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(15, 15, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            tabText.Controls.Add(_previewText);

            _previewTabs.TabPages.Add(tabImage);
            _previewTabs.TabPages.Add(tabKeylog);
            _previewTabs.TabPages.Add(tabText);

            Controls.Add(topPanel);
            Controls.Add(_grid);
            Controls.Add(_previewTabs);
        }

        private void LoadFilters()
        {
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT u.id, u.full_name FROM log_entries l JOIN users u ON l.user_id = u.id ORDER BY u.full_name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    _cmbUser.Items.Add(new ComboItem<int?>(reader.GetInt32(0), reader.GetString(1)));
            }
            catch { }
            _cmbUser.SelectedIndex = 0;
            _cmbType.SelectedIndex = 0;
            _dtpFrom.Value = DateTime.Now.AddDays(-7);
            _dtpTo.Value = DateTime.Now.AddDays(1);
        }

        private void Search()
        {
            _grid.Rows.Clear();
            _previewBox.Image = null;
            _txtKeylogContent.Text = "";
            _previewText.Text = "";

            try
            {
                var userId = (_cmbUser.SelectedItem as ComboItem<int?>)?.Value;
                var type = _cmbType.SelectedIndex > 0 ? _cmbType.Text : null;

                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();

                var conditions = new List<string>();
                if (userId.HasValue) conditions.Add("l.user_id = @uid");
                if (!string.IsNullOrEmpty(type)) conditions.Add("l.type = @type");
                conditions.Add("l.timestamp >= @from");
                conditions.Add("l.timestamp <= @to");

                var where = string.Join(" AND ", conditions);
                cmd.CommandText = $@"SELECT l.timestamp, COALESCE(u.full_name, 'Sistema'), l.type, COALESCE(l.description, ''), COALESCE(l.file_path, '')
                                     FROM log_entries l LEFT JOIN users u ON l.user_id = u.id
                                     WHERE {where} ORDER BY l.timestamp DESC LIMIT 500";

                if (userId.HasValue) cmd.Parameters.AddWithValue("@uid", userId.Value);
                if (!string.IsNullOrEmpty(type)) cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@from", _dtpFrom.Value);
                cmd.Parameters.AddWithValue("@to", _dtpTo.Value.AddDays(1));

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _grid.Rows.Add(
                        reader.GetDateTime(0).ToString("dd/MM/yyyy HH:mm:ss"),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4)
                    );
                }
            }
            catch { }
        }

        private void ShowPreview()
        {
            _previewBox.Image = null;
            _txtKeylogContent.Text = "";
            _previewText.Text = "";

            if (_grid.SelectedRows.Count == 0) return;

            var row = _grid.SelectedRows[0];
            var type = row.Cells[2].Value?.ToString() ?? "";
            var filePath = row.Cells[4].Value?.ToString() ?? "";
            var desc = row.Cells[3].Value?.ToString() ?? "";
            var timestamp = row.Cells[0].Value?.ToString() ?? "";
            var user = row.Cells[1].Value?.ToString() ?? "";

            _previewText.Text = $"Data/Hora: {timestamp}\r\nUsuário: {user}\r\nTipo: {type}\r\nDescrição: {desc}\r\nArquivo: {filePath}";

            if (type == "screenshot" || type == "camera")
            {
                _previewTabs.SelectedIndex = 0;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try { _previewBox.Image = Image.FromFile(filePath); } catch { }
                }
            }
            else if (type == "keylog")
            {
                _previewTabs.SelectedIndex = 1;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        var content = File.ReadAllText(filePath);
                        var lines = content.Split('\n');
                        _txtKeylogContent.Text = string.Join("\r\n", lines.TakeLast(500));
                    }
                    catch { }
                }
            }
            else
            {
                _previewTabs.SelectedIndex = 2;
            }
        }

        private void ExportLogs()
        {
            using var sfd = new SaveFileDialog
            {
                Filter = "Arquivo ZIP|*.zip",
                FileName = $"logs_export_{DateTime.Now:yyyyMMdd}.zip",
                Title = "Exportar logs"
            };
            if (sfd.ShowDialog() != DialogResult.OK) return;

            try
            {
                using var zip = ZipFile.Open(sfd.FileName, ZipArchiveMode.Create);

                var csv = "Data/Hora;Usuário;Tipo;Descrição;Arquivo\r\n";
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    csv += $"{row.Cells[0].Value};{row.Cells[1].Value};{row.Cells[2].Value};{row.Cells[3].Value};{row.Cells[4].Value}\r\n";
                }
                var entry = zip.CreateEntry("logs.csv");
                using (var writer = new StreamWriter(entry.Open()))
                    writer.Write(csv);

                foreach (DataGridViewRow row in _grid.Rows)
                {
                    var fp = row.Cells[4].Value?.ToString();
                    if (!string.IsNullOrEmpty(fp) && File.Exists(fp))
                    {
                        try
                        {
                            zip.CreateEntryFromFile(fp, Path.GetFileName(fp));
                        }
                        catch { }
                    }
                }

                MessageBox.Show($"Logs exportados para:\n{sfd.FileName}", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar: {ex.Message}", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CleanupLogs()
        {
            if (MessageBox.Show("Limpar logs antigos conforme a retenção configurada?", _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _db.CleanupOldLogs();
                _db.InsertLog(null, "event", null, "Limpeza manual de logs executada");
                MessageBox.Show("Logs antigos removidos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Search();
            }
        }

        private class ComboItem<T>
        {
            public T Value { get; }
            public string Text { get; }
            public ComboItem(T value, string text) { Value = value; Text = text; }
            public override string ToString() => Text;
        }
    }
}
