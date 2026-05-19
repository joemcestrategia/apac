using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class AllowedSitesTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly SiteService _siteService;
        private readonly ProfileService _profileService;
        private DataGridView _grid;
        private Button _btnAdd;
        private Button _btnDelete;
        private Button _btnImport;
        private Button _btnExport;
        private ComboBox _cmbProfileFilter;
        private TextBox _txtTestUrl;
        private Button _btnTestUrl;
        private Label _lblTestResult;

        public AllowedSitesTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            _siteService = new SiteService(db);
            _profileService = new ProfileService(db);
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            var topPanel = new Panel { Height = 50, Dock = DockStyle.Top, BackColor = Color.Transparent };

            var lblFilter = new Label { Text = "Perfil:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(0, 12), AutoSize = true };
            _cmbProfileFilter = new ComboBox { Location = new Point(50, 10), Size = new Size(200, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White };
            _cmbProfileFilter.Items.Add(new ComboItem<int?>(null, "Todos (Global)"));
            try
            {
                var profiles = _profileService.GetAll();
                foreach (var p in profiles)
                    _cmbProfileFilter.Items.Add(new ComboItem<int?>(p.Id, p.Name));
            }
            catch { }
            _cmbProfileFilter.SelectedIndex = 0;
            _cmbProfileFilter.SelectedIndexChanged += (s, e) => RefreshList();

            _btnAdd = CreateButton("+ Adicionar", 270, Color.FromArgb(124, 58, 237));
            _btnDelete = CreateButton("✕ Remover", 425, Color.FromArgb(200, 50, 50));
            _btnImport = CreateButton("Importar .txt", 580, Color.FromArgb(59, 130, 246));
            _btnExport = CreateButton("Exportar", 750, Color.FromArgb(245, 158, 11));
            _btnAdd.Click += (s, e) => AddSite();
            _btnDelete.Click += (s, e) => DeleteSite();
            _btnImport.Click += (s, e) => ImportSites();
            _btnExport.Click += (s, e) => ExportSites();

            topPanel.Controls.AddRange(new Control[] { lblFilter, _cmbProfileFilter, _btnAdd, _btnDelete, _btnImport, _btnExport });

            _grid = new DataGridView
            {
                Location = new Point(0, 60),
                Size = new Size(1000, 380),
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
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            _grid.Columns.Add("Id", "ID");
            _grid.Columns.Add("Url", "URL/Domínio");
            _grid.Columns.Add("Profile", "Perfil");
            _grid.Columns.Add("Global", "Global");
            _grid.Columns.Add("Added", "Adicionado em");

            var testPanel = new Panel
            {
                Location = new Point(0, 450),
                Size = new Size(1000, 200),
                BackColor = Color.FromArgb(22, 22, 45),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var lblTest = new Label { Text = "Testar URL:", Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.FromArgb(167, 139, 250), Location = new Point(15, 15), AutoSize = true };
            _txtTestUrl = new TextBox { Location = new Point(15, 45), Size = new Size(500, 30), Font = new Font("Segoe UI", 11), BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            _btnTestUrl = new Button { Text = "Testar", Location = new Point(525, 45), Size = new Size(100, 30), Font = new Font("Segoe UI", 10, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand };
            _btnTestUrl.FlatAppearance.BorderSize = 0;
            _btnTestUrl.Click += (s, e) => TestUrl();
            _lblTestResult = new Label { Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Color.White, Location = new Point(15, 95), AutoSize = true, MaximumSize = new Size(950, 100) };
            testPanel.Controls.AddRange(new Control[] { lblTest, _txtTestUrl, _btnTestUrl, _lblTestResult });

            Controls.Add(topPanel);
            Controls.Add(_grid);
            Controls.Add(testPanel);
        }

        private Button CreateButton(string text, int x, Color backColor)
        {
            var btn = new Button
            {
                Text = text, Location = new Point(x, 7), Size = new Size(140, 36),
                Font = new Font("Segoe UI", 10, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = backColor, ForeColor = Color.White, Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void RefreshList()
        {
            _grid.Rows.Clear();
            try
            {
                int? filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
                var sites = _siteService.GetForProfile(filterProfile);
                foreach (var s in sites)
                {
                    var profileName = s.IsGlobal ? "Global" : (_profileService.GetAll().FirstOrDefault(p => p.Id == s.ProfileId)?.Name ?? "-");
                    _grid.Rows.Add(s.Id, s.UrlPattern, profileName, s.IsGlobal ? "Sim" : "Não", s.CreatedAt.ToString("dd/MM/yyyy"));
                }
            }
            catch { }
        }

        private void AddSite()
        {
            var input = Microsoft.VisualBasic.Interaction.InputBox("Digite a URL ou domínio (ex: wikipedia.org):", "Adicionar Site", "");
            if (string.IsNullOrWhiteSpace(input)) return;

            var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
            var isGlobal = !filterProfile.HasValue;
            _siteService.Add(input.Trim(), filterProfile, isGlobal);
            _db.InsertLog(null, "event", null, $"Site adicionado: {input}");
            RefreshList();
        }

        private void DeleteSite()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                MessageBox.Show("Selecione um site para remover.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var id = Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value);
            var url = _grid.SelectedRows[0].Cells[1].Value.ToString();
            if (MessageBox.Show($"Remover \"{url}\"?", _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _siteService.Delete(id);
                RefreshList();
            }
        }

        private void ImportSites()
        {
            using var ofd = new OpenFileDialog { Filter = "Arquivo de texto|*.txt", Title = "Importar lista de sites" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
            var isGlobal = !filterProfile.HasValue;
            _siteService.ImportFromFile(ofd.FileName, filterProfile, isGlobal);
            MessageBox.Show("Importação concluída.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshList();
        }

        private void ExportSites()
        {
            using var sfd = new SaveFileDialog { Filter = "Arquivo de texto|*.txt", Title = "Exportar lista de sites", FileName = "sites_permitidos.txt" };
            if (sfd.ShowDialog() != DialogResult.OK) return;
            _siteService.ExportToFile(sfd.FileName);
            MessageBox.Show("Exportação concluída.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void TestUrl()
        {
            var url = _txtTestUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
                if (!url.StartsWith("http"))
                    url = "https://" + url;

                var allowed = _siteService.IsUrlAllowed(url, filterProfile);
                _lblTestResult.Text = allowed
                    ? $"✓ {url} — PERMITIDO"
                    : $"✕ {url} — BLOQUEADO (não está na lista de sites permitidos)";
                _lblTestResult.ForeColor = allowed ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68);
            }
            catch
            {
                _lblTestResult.Text = "URL inválida.";
                _lblTestResult.ForeColor = Color.FromArgb(239, 68, 68);
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
