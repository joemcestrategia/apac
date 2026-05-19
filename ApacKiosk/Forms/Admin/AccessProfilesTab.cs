using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Models;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class AccessProfilesTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly ProfileService _profileService;
        private readonly SiteService _siteService;
        private DataGridView _grid;
        private Button _btnAdd;
        private Button _btnEdit;
        private Button _btnDelete;
        private Panel _detailPanel;
        private Label _detailTitle;
        private ComboBox _cmbProfileSelect;
        private DataGridView _timeRulesGrid;
        private DataGridView _profileSitesGrid;
        private Button _btnAddTimeRule;
        private Button _btnRemoveTimeRule;
        private Button _btnAddSite;
        private Button _btnRemoveSite;

        public AccessProfilesTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            _profileService = new ProfileService(db);
            _siteService = new SiteService(db);
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            var topPanel = new Panel { Height = 50, Dock = DockStyle.Top, BackColor = Color.Transparent };

            _btnAdd = CreateButton("+ Novo Perfil", 0, Color.FromArgb(124, 58, 237));
            _btnEdit = CreateButton("✎ Editar", 160, Color.FromArgb(59, 130, 246));
            _btnDelete = CreateButton("✕ Excluir", 310, Color.FromArgb(200, 50, 50));
            _btnAdd.Click += (s, e) => AddProfile();
            _btnEdit.Click += (s, e) => EditProfile();
            _btnDelete.Click += (s, e) => DeleteProfile();
            topPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnDelete });

            _grid = new DataGridView
            {
                Location = new Point(0, 60),
                Size = new Size(400, 520),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
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
            _grid.Columns.Add("MaxSession", "Sessão (min)");
            _grid.Columns.Add("PauseAfter", "Pausa após (min)");
            _grid.Columns.Add("PauseDuration", "Duração pausa (min)");
            _grid.SelectionChanged += (s, e) => LoadDetail();

            _detailPanel = new Panel
            {
                Location = new Point(420, 60),
                Size = new Size(580, 520),
                BackColor = Color.FromArgb(22, 22, 45),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true
            };

            _detailTitle = new Label
            {
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(15, 10),
                AutoSize = true,
                Text = "Selecione um perfil para ver detalhes"
            };

            var timeRuleLbl = new Label
            {
                Text = "Regras de Horário",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(15, 50),
                AutoSize = true
            };

            _btnAddTimeRule = CreateSmallButton("+", 140, 48, Color.FromArgb(124, 58, 237));
            _btnRemoveTimeRule = CreateSmallButton("-", 180, 48, Color.FromArgb(200, 50, 50));
            _btnAddTimeRule.Click += (s, e) => AddTimeRule();
            _btnRemoveTimeRule.Click += (s, e) => RemoveTimeRule();

            _timeRulesGrid = new DataGridView
            {
                Location = new Point(15, 80),
                Size = new Size(540, 150),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false
            };
            _timeRulesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _timeRulesGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _timeRulesGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _timeRulesGrid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _timeRulesGrid.DefaultCellStyle.ForeColor = Color.White;
            _timeRulesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            _timeRulesGrid.Columns.Add("Day", "Dia");
            _timeRulesGrid.Columns.Add("Start", "Início");
            _timeRulesGrid.Columns.Add("End", "Fim");

            var sitesLbl = new Label
            {
                Text = "Sites do Perfil",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(15, 245),
                AutoSize = true
            };

            _btnAddSite = CreateSmallButton("+", 140, 243, Color.FromArgb(124, 58, 237));
            _btnRemoveSite = CreateSmallButton("-", 180, 243, Color.FromArgb(200, 50, 50));
            _btnAddSite.Click += (s, e) => AddSiteToProfile();
            _btnRemoveSite.Click += (s, e) => RemoveSiteFromProfile();

            _profileSitesGrid = new DataGridView
            {
                Location = new Point(15, 275),
                Size = new Size(540, 220),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false
            };
            _profileSitesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _profileSitesGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _profileSitesGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _profileSitesGrid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _profileSitesGrid.DefaultCellStyle.ForeColor = Color.White;
            _profileSitesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            _profileSitesGrid.Columns.Add("Url", "URL/Domínio");

            _detailPanel.Controls.AddRange(new Control[] { _detailTitle, timeRuleLbl, _btnAddTimeRule,
                _btnRemoveTimeRule, _timeRulesGrid, sitesLbl, _btnAddSite, _btnRemoveSite, _profileSitesGrid });

            Controls.Add(topPanel);
            Controls.Add(_grid);
            Controls.Add(_detailPanel);
        }

        private Button CreateButton(string text, int x, Color backColor)
        {
            var btn = new Button
            {
                Text = text, Location = new Point(x, 5), Size = new Size(145, 40),
                Font = new Font("Segoe UI", 10, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = backColor, ForeColor = Color.White, Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private Button CreateSmallButton(string text, int x, int y, Color backColor)
        {
            var btn = new Button
            {
                Text = text, Location = new Point(x, y), Size = new Size(35, 28),
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
                var profiles = _profileService.GetAll();
                foreach (var p in profiles)
                {
                    _grid.Rows.Add(p.Id, p.Name, p.MaxSessionMinutes == 0 ? "Ilimitado" : p.MaxSessionMinutes.ToString(),
                        p.PauseAfterMinutes == 0 ? "-" : p.PauseAfterMinutes.ToString(),
                        p.MandatoryPauseMinutes == 0 ? "-" : p.MandatoryPauseMinutes.ToString());
                }
            }
            catch { }
        }

        private int? GetSelectedProfileId()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            return Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value);
        }

        private void LoadDetail()
        {
            _timeRulesGrid.Rows.Clear();
            _profileSitesGrid.Rows.Clear();
            var pid = GetSelectedProfileId();
            if (!pid.HasValue) { _detailTitle.Text = "Selecione um perfil para ver detalhes"; return; }

            _detailTitle.Text = $"Perfil: {_grid.SelectedRows[0].Cells[1].Value}";
            try
            {
                var rules = _profileService.GetTimeRules(pid.Value);
                var dayNames = new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" };
                foreach (var r in rules)
                    _timeRulesGrid.Rows.Add(dayNames[r.DayOfWeek], r.StartTime.ToString(@"hh\:mm"), r.EndTime.ToString(@"hh\:mm"));

                var sites = _siteService.GetForProfile(pid.Value);
                foreach (var s in sites.Where(s => s.ProfileId == pid.Value))
                    _profileSitesGrid.Rows.Add(s.UrlPattern);
            }
            catch { }
        }

        private void AddProfile()
        {
            var dialog = CreateProfileDialog(null);
            if (dialog.ShowDialog() == DialogResult.OK) RefreshList();
        }

        private void EditProfile()
        {
            var pid = GetSelectedProfileId();
            if (!pid.HasValue) { MessageBox.Show("Selecione um perfil.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var dialog = CreateProfileDialog(pid.Value);
            if (dialog.ShowDialog() == DialogResult.OK) RefreshList();
        }

        private void DeleteProfile()
        {
            var pid = GetSelectedProfileId();
            if (!pid.HasValue) { MessageBox.Show("Selecione um perfil.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var name = _grid.SelectedRows[0].Cells[1].Value.ToString();
            if (MessageBox.Show($"Excluir o perfil \"{name}\"?", _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _profileService.Delete(pid.Value);
                _db.InsertLog(null, "event", null, $"Perfil excluído: {name}");
                RefreshList();
            }
        }

        private Form CreateProfileDialog(int? profileId)
        {
            var form = new Form
            {
                Text = profileId.HasValue ? "Editar Perfil" : "Novo Perfil",
                Size = new Size(480, 380),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45),
                MaximizeBox = false, MinimizeBox = false
            };

            var y = 15;
            var txtName = AddField(form, "Nome do Perfil:", ref y);
            var numMaxSession = AddNumericField(form, "Duração Máxima da Sessão (min, 0=ilimitado):", ref y, 0, 1440);
            var numPauseAfter = AddNumericField(form, "Pausa Obrigatória Após (min, 0=desativado):", ref y, 0, 1440);
            var numPauseDuration = AddNumericField(form, "Duração da Pausa (min):", ref y, 0, 120);
            var txtHomepage = AddField(form, "URL da Página Inicial:", ref y);

            if (profileId.HasValue)
            {
                var profile = _profileService.GetById(profileId.Value);
                if (profile != null)
                {
                    txtName.Text = profile.Name;
                    numMaxSession.Value = profile.MaxSessionMinutes;
                    numPauseAfter.Value = profile.PauseAfterMinutes;
                    numPauseDuration.Value = profile.MandatoryPauseMinutes;
                    txtHomepage.Text = profile.HomepageUrl ?? "";
                }
            }

            var btnSave = new Button
            {
                Text = "Salvar", Location = new Point(140, y + 10), Size = new Size(200, 40),
                Font = new Font("Segoe UI", 12, FontStyle.Bold), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show("Digite o nome do perfil.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var homepage = string.IsNullOrWhiteSpace(txtHomepage.Text) ? "https://www.google.com" : txtHomepage.Text.Trim();
                if (profileId.HasValue)
                    _profileService.Update(profileId.Value, txtName.Text.Trim(), (int)numMaxSession.Value, (int)numPauseDuration.Value, (int)numPauseAfter.Value, homepage);
                else
                    _profileService.Create(txtName.Text.Trim(), (int)numMaxSession.Value, (int)numPauseDuration.Value, (int)numPauseAfter.Value, homepage);

                _db.InsertLog(null, "event", null, $"Perfil {(profileId.HasValue ? "editado" : "criado")}: {txtName.Text}");
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(btnSave);
            return form;
        }

        private TextBox AddField(Form form, string label, ref int y)
        {
            var lbl = new Label { Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var txt = new TextBox { Location = new Point(20, y), Size = new Size(420, 28), Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            y += 42;
            form.Controls.Add(lbl);
            return txt;
        }

        private NumericUpDown AddNumericField(Form form, string label, ref int y, int min, int max)
        {
            var lbl = new Label { Text = label, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var num = new NumericUpDown { Location = new Point(20, y), Size = new Size(120, 28), Minimum = min, Maximum = max, Font = new Font("Segoe UI", 10), BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            y += 42;
            form.Controls.Add(lbl);
            return num;
        }

        private void AddTimeRule()
        {
            var pid = GetSelectedProfileId();
            if (!pid.HasValue) { MessageBox.Show("Selecione um perfil.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var form = new Form
            {
                Text = "Adicionar Regra de Horário", Size = new Size(350, 280),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45), MaximizeBox = false, MinimizeBox = false
            };

            var y = 15;
            var lblDay = new Label { Text = "Dia da Semana:", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var cmbDay = new ComboBox { Location = new Point(20, y), Size = new Size(290, 30), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 26, 46), ForeColor = Color.White };
            cmbDay.Items.AddRange(new[] { "Domingo", "Segunda", "Terça", "Quarta", "Quinta", "Sexta", "Sábado" });
            cmbDay.SelectedIndex = 1;
            y += 45;

            var lblStart = new Label { Text = "Início (HH:MM):", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var dtpStart = new DateTimePicker { Location = new Point(20, y), Size = new Size(120, 28), Format = DateTimePickerFormat.Time, ShowUpDown = true, Font = new Font("Segoe UI", 10) };
            dtpStart.CalendarMonthBackground = Color.FromArgb(26, 26, 46);
            y += 45;

            var lblEnd = new Label { Text = "Fim (HH:MM):", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 220), Location = new Point(20, y), AutoSize = true };
            y += 25;
            var dtpEnd = new DateTimePicker { Location = new Point(20, y), Size = new Size(120, 28), Format = DateTimePickerFormat.Time, ShowUpDown = true, Font = new Font("Segoe UI", 10) };
            dtpEnd.Value = dtpEnd.Value.AddHours(8);

            var btnSave = new Button { Text = "Salvar", Location = new Point(100, y + 40), Size = new Size(150, 35), Font = new Font("Segoe UI", 11, FontStyle.Bold), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(124, 58, 237), ForeColor = Color.White, Cursor = Cursors.Hand };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                var rules = _profileService.GetTimeRules(pid.Value);
                rules.Add(new TimeRule { ProfileId = pid.Value, DayOfWeek = cmbDay.SelectedIndex, StartTime = dtpStart.Value.TimeOfDay, EndTime = dtpEnd.Value.TimeOfDay });
                _profileService.SaveTimeRules(pid.Value, rules);
                form.Close();
                LoadDetail();
            };

            form.Controls.AddRange(new Control[] { lblDay, cmbDay, lblStart, dtpStart, lblEnd, dtpEnd, btnSave });
            form.ShowDialog();
        }

        private void RemoveTimeRule()
        {
            var pid = GetSelectedProfileId();
            if (!pid.HasValue || _timeRulesGrid.SelectedRows.Count == 0) return;
            var rules = _profileService.GetTimeRules(pid.Value);
            var idx = _timeRulesGrid.SelectedRows[0].Index;
            if (idx < rules.Count)
            {
                rules.RemoveAt(idx);
                _profileService.SaveTimeRules(pid.Value, rules);
                LoadDetail();
            }
        }

        private void AddSiteToProfile()
        {
            var pid = GetSelectedProfileId();
            if (!pid.HasValue) { MessageBox.Show("Selecione um perfil.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var input = Microsoft.VisualBasic.Interaction.InputBox("Digite a URL ou domínio:", "Adicionar Site", "");
            if (!string.IsNullOrWhiteSpace(input))
            {
                _siteService.Add(input.Trim(), pid.Value, false);
                _db.InsertLog(null, "event", null, $"Site adicionado ao perfil {pid}: {input}");
                LoadDetail();
            }
        }

        private void RemoveSiteFromProfile()
        {
            if (_profileSitesGrid.SelectedRows.Count == 0) return;
            var url = _profileSitesGrid.SelectedRows[0].Cells[0].Value.ToString();
            var sites = _siteService.GetForProfile(GetSelectedProfileId());
            var site = sites.FirstOrDefault(s => s.UrlPattern == url && s.ProfileId == GetSelectedProfileId());
            if (site != null)
            {
                _siteService.Delete(site.Id);
                LoadDetail();
            }
        }
    }
}
