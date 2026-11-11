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
            var _btnSearchWeb = CreateButton("🔍 Pesquisar na Web", 425, Color.FromArgb(16, 185, 129));
            _btnDelete = CreateButton("✕ Remover", 580, Color.FromArgb(200, 50, 50));
            _btnImport = CreateButton("Importar .txt", 735, Color.FromArgb(59, 130, 246));
            _btnExport = CreateButton("Exportar", 890, Color.FromArgb(245, 158, 11));
            _btnAdd.Click += (s, e) => AddSite();
            _btnSearchWeb.Click += (s, e) => SearchWeb();
            _btnDelete.Click += (s, e) => DeleteSite();
            _btnImport.Click += (s, e) => ImportSites();
            _btnExport.Click += (s, e) => ExportSites();

            topPanel.Controls.AddRange(new Control[] { lblFilter, _cmbProfileFilter, _btnAdd, _btnSearchWeb, _btnDelete, _btnImport, _btnExport });

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
            _grid.Columns.Add("Icon", "Ícone");
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
                    _grid.Rows.Add(s.Id, s.UrlPattern, string.IsNullOrEmpty(s.IconPath) ? "(padrão)" : Path.GetFileName(s.IconPath), profileName, s.IsGlobal ? "Sim" : "Não", s.CreatedAt.ToString("dd/MM/yyyy"));
                }
            }
            catch { }
        }

        private void AddSite()
        {
            var form = new Form
            {
                Text = "Adicionar Site",
                Size = new Size(550, 270),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45),
                MaximizeBox = false, MinimizeBox = false, TopMost = true
            };
            form.Owner = this.FindForm() as Form;

            var lbl = new Label
            {
                Text = "Digite a URL ou domínio (ex: wikipedia.org):",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, 20),
                AutoSize = true
            };

            var txtInput = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(490, 28),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblIcon = new Label
            {
                Text = "Ícone personalizado (opcional):",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, 90),
                AutoSize = true
            };

            var txtIconPath = new TextBox
            {
                Location = new Point(20, 118),
                Size = new Size(380, 28),
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true
            };

            var btnBrowseIcon = new Button
            {
                Text = "Procurar",
                Location = new Point(410, 118),
                Size = new Size(100, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBrowseIcon.FlatAppearance.BorderSize = 0;
            btnBrowseIcon.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Imagens|*.png;*.ico;*.jpg;*.jpeg;*.bmp", Title = "Selecionar ícone" };
                if (ofd.ShowDialog(this.FindForm()) == DialogResult.OK)
                    txtIconPath.Text = ofd.FileName;
            };

            string? iconPath = null;

            var btnOk = new Button
            {
                Text = "Adicionar",
                Location = new Point(180, 180),
                Size = new Size(180, 35),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(txtInput.Text))
                {
                    var input = txtInput.Text.Trim();
                    var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
                    var isGlobal = !filterProfile.HasValue;
                    if (!string.IsNullOrWhiteSpace(txtIconPath.Text))
                        iconPath = txtIconPath.Text.Trim();
                    _siteService.Add(input, filterProfile, isGlobal, iconPath);
                    _db.InsertLog(null, "event", null, $"Site adicionado: {input}");
                    RefreshList();
                }
                form.Close();
            };

            var btnClearIcon = new Button
            {
                Text = "Limpar",
                Location = new Point(20, 180),
                Size = new Size(100, 35),
                Font = new Font("Segoe UI", 10),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(200, 50, 50),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnClearIcon.FlatAppearance.BorderSize = 0;
            btnClearIcon.Click += (s, e) => { txtIconPath.Text = ""; iconPath = null; };

            txtInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) btnOk.PerformClick(); };
            form.AcceptButton = btnOk;

            form.Controls.AddRange(new Control[] { lbl, txtInput, lblIcon, txtIconPath, btnBrowseIcon, btnClearIcon, btnOk });
            form.ShowDialog(this.FindForm());
        }

        private void SearchWeb()
        {
            using var searchForm = new SearchSiteForm();
            if (searchForm.ShowDialog(this.FindForm()) == DialogResult.OK && !string.IsNullOrWhiteSpace(searchForm.SelectedDomain))
            {
                var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
                var isGlobal = !filterProfile.HasValue;
                _siteService.Add(searchForm.SelectedDomain, filterProfile, isGlobal, null);
                _db.InsertLog(null, "event", null, $"Site adicionado via pesquisa: {searchForm.SelectedDomain}");
                RefreshList();
            }
        }

        private void DeleteSite()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                KioskDialog.ShowMessage("Selecione um site para remover.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning, this);
                return;
            }
            var id = Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value);
            var url = _grid.SelectedRows[0].Cells[1].Value.ToString();
            if (KioskDialog.ShowMessage($"Remover \"{url}\"?", _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, this) == DialogResult.Yes)
            {
                _siteService.Delete(id);
                RefreshList();
            }
        }

        private void ImportSites()
        {
            using var ofd = new OpenFileDialog { Filter = "Arquivo de texto|*.txt", Title = "Importar lista de sites" };
            if (ofd.ShowDialog(this.FindForm()) != DialogResult.OK) return;

            var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
            var isGlobal = !filterProfile.HasValue;
            _siteService.ImportFromFile(ofd.FileName, filterProfile, isGlobal);
            KioskDialog.ShowMessage("Importação concluída.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
            RefreshList();
        }

        private void ExportSites()
        {
            using var sfd = new SaveFileDialog { Filter = "Arquivo de texto|*.txt", Title = "Exportar lista de sites", FileName = "sites_permitidos.txt" };
            if (sfd.ShowDialog(this.FindForm()) != DialogResult.OK) return;
            _siteService.ExportToFile(sfd.FileName);
            KioskDialog.ShowMessage("Exportação concluída.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information, this);
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
