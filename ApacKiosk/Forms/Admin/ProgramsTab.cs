using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class ProgramsTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly ProgramService _programService;
        private readonly ProfileService _profileService;
        private DataGridView _grid;
        private Button _btnAdd;
        private Button _btnDelete;
        private ComboBox _cmbProfileFilter;

        public ProgramsTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            _programService = new ProgramService(db);
            _profileService = new ProfileService(db);
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
            _programService.SeedDefaults();
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
                foreach (var p in _profileService.GetAll())
                    _cmbProfileFilter.Items.Add(new ComboItem<int?>(p.Id, p.Name));
            }
            catch { }
            _cmbProfileFilter.SelectedIndex = 0;
            _cmbProfileFilter.SelectedIndexChanged += (s, e) => RefreshList();

            _btnAdd = CreateButton("+ Adicionar", 270, Color.FromArgb(124, 58, 237));
            _btnDelete = CreateButton("✕ Remover", 425, Color.FromArgb(200, 50, 50));
            _btnAdd.Click += (s, e) => AddProgram();
            _btnDelete.Click += (s, e) => DeleteProgram();

            topPanel.Controls.AddRange(new Control[] { lblFilter, _cmbProfileFilter, _btnAdd, _btnDelete });

            _grid = new DataGridView
            {
                Location = new Point(0, 60),
                Size = new Size(1000, 500),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                EnableHeadersVisualStyles = false
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            _grid.Columns.Add("Id", "ID");
            _grid.Columns.Add("Name", "Nome");
            _grid.Columns.Add("Path", "Caminho");
            _grid.Columns.Add("Args", "Argumentos");
            _grid.Columns.Add("Profile", "Perfil");
            _grid.Columns.Add("Global", "Global");

            Controls.Add(topPanel);
            Controls.Add(_grid);
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
                var filterProfile = (_cmbProfileFilter.SelectedItem as ComboItem<int?>)?.Value;
                var programs = _programService.GetForProfile(filterProfile);
                foreach (var p in programs)
                {
                    var profileName = p.IsGlobal ? "Global" : (_profileService.GetAll().FirstOrDefault(x => x.Id == p.ProfileId)?.Name ?? "-");
                    _grid.Rows.Add(p.Id, p.Name, p.ExecutablePath, p.Arguments ?? "", profileName, p.IsGlobal ? "Sim" : "Não");
                }
            }
            catch { }
        }

        private void AddProgram()
        {
            var form = new Form
            {
                Text = "Adicionar Programa", Size = new Size(520, 320),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45), MaximizeBox = false, MinimizeBox = false
            };

            var y = 15;
            var txtName = AddField(form, "Nome do Programa:", ref y);
            var txtPath = AddField(form, "Caminho do Executável:", ref y);
            var btnBrowseExe = new Button { Text = "Procurar", Location = new Point(380, y - 30), Size = new Size(100, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(59, 130, 246), ForeColor = Color.White, Cursor = Cursors.Hand };
            btnBrowseExe.FlatAppearance.BorderSize = 0;
            btnBrowseExe.Click += (s, e) => { using var ofd = new OpenFileDialog { Filter = "Executáveis|*.exe" }; if (ofd.ShowDialog() == DialogResult.OK) txtPath.Text = ofd.FileName; };
            form.Controls.Add(btnBrowseExe);

            var txtArgs = AddField(form, "Argumentos (opcional):", ref y);

            var lblProfile = new Label { Text = "Perfil:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var cmbProfile = new ComboBox { Location = new Point(20, y), Size = new Size(460, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White };
            cmbProfile.Items.Add(new ComboItem<int?>(null, "Global (todos os perfis)"));
            foreach (var p in _profileService.GetAll())
                cmbProfile.Items.Add(new ComboItem<int?>(p.Id, p.Name));
            cmbProfile.SelectedIndex = 0;
            y += 45;

            var btnSave = new Button { Text = "Salvar", Location = new Point(160, y + 5), Size = new Size(200, 38), Font = new Font("Segoe UI", 12, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtPath.Text))
                { MessageBox.Show("Preencha Nome e Caminho.", _config.DisplayName); return; }
                var prof = (cmbProfile.SelectedItem as ComboItem<int?>)?.Value;
                _programService.Add(txtName.Text.Trim(), txtPath.Text.Trim(),
                    string.IsNullOrWhiteSpace(txtArgs.Text) ? null : txtArgs.Text.Trim(),
                    prof, !prof.HasValue);
                _db.InsertLog(null, "event", null, $"Programa adicionado: {txtName.Text}");
                form.Close();
                RefreshList();
            };

            form.Controls.AddRange(new Control[] { txtName, txtPath, txtArgs, lblProfile, cmbProfile, btnSave });
            form.ShowDialog();
        }

        private void DeleteProgram()
        {
            if (_grid.SelectedRows.Count == 0) { MessageBox.Show("Selecione um programa.", _config.DisplayName); return; }
            var id = Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value);
            var name = _grid.SelectedRows[0].Cells[1].Value.ToString();
            if (MessageBox.Show($"Remover \"{name}\"?", _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _programService.Delete(id);
                _db.InsertLog(null, "event", null, $"Programa removido: {name}");
                RefreshList();
            }
        }

        private TextBox AddField(Form form, string label, ref int y)
        {
            var lbl = new Label { Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var txt = new TextBox { Location = new Point(20, y), Size = new Size(350, 28), Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            y += 35;
            form.Controls.Add(lbl);
            return txt;
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
