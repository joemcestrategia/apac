using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ApacKiosk.Database;
using ApacKiosk.Models;
using ApacKiosk.Services;
using ApacKiosk.Utils;

namespace ApacKiosk.Forms.Admin
{
    public class UserManagementTab : UserControl
    {
        private readonly DatabaseManager _db;
        private readonly ConfigManager _config;
        private readonly UserService _userService;
        private DataGridView _grid;
        private Button _btnAdd;
        private Button _btnEdit;
        private Button _btnDelete;
        private Button _btnToggle;

        public UserManagementTab(DatabaseManager db, ConfigManager config)
        {
            _db = db;
            _config = config;
            _userService = new UserService(db);
            BackColor = Color.FromArgb(15, 15, 35);
            InitializeComponent();
            RefreshList();
        }

        private void InitializeComponent()
        {
            var topPanel = new Panel
            {
                Height = 50,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent
            };

            _btnAdd = CreateButton("+ Novo Usuário", 0, Color.FromArgb(124, 58, 237));
            _btnEdit = CreateButton("✎ Editar", 160, Color.FromArgb(59, 130, 246));
            _btnDelete = CreateButton("✕ Excluir", 310, Color.FromArgb(200, 50, 50));
            _btnToggle = CreateButton("Ativar/Desativar", 460, Color.FromArgb(245, 158, 11));

            _btnAdd.Click += (s, e) => AddUser();
            _btnEdit.Click += (s, e) => EditUser();
            _btnDelete.Click += (s, e) => DeleteUser();
            _btnToggle.Click += (s, e) => ToggleUser();

            topPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnDelete, _btnToggle });

            _grid = new DataGridView
            {
                Location = new Point(0, 60),
                Size = new Size(1000, 520),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
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
            _grid.Columns.Add("FullName", "Nome Completo");
            _grid.Columns.Add("Username", "Usuário");
            _grid.Columns.Add("Profile", "Perfil");
            _grid.Columns.Add("Active", "Ativo");
            _grid.Columns.Add("Created", "Criado em");

            Controls.Add(topPanel);
            Controls.Add(_grid);
        }

        private Button CreateButton(string text, int x, Color backColor)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, 5),
                Size = new Size(145, 40),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void RefreshList()
        {
            _grid.Rows.Clear();
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT u.id, u.full_name, u.username, COALESCE(p.name, '-'), u.is_active, u.created_at
                                   FROM users u LEFT JOIN access_profiles p ON u.profile_id = p.id ORDER BY u.full_name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    _grid.Rows.Add(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetBoolean(4) ? "Sim" : "Não",
                        reader.GetDateTime(5).ToString("dd/MM/yyyy")
                    );
                }
            }
            catch { }
        }

        private int? GetSelectedUserId()
        {
            if (_grid.SelectedRows.Count == 0) return null;
            return Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value);
        }

        private void AddUser()
        {
            var dialog = CreateUserDialog(null);
            if (dialog.ShowDialog() == DialogResult.OK)
                RefreshList();
        }

        private void EditUser()
        {
            var userId = GetSelectedUserId();
            if (!userId.HasValue)
            {
                MessageBox.Show("Selecione um usuário para editar.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var dialog = CreateUserDialog(userId.Value);
            if (dialog.ShowDialog() == DialogResult.OK)
                RefreshList();
        }

        private void DeleteUser()
        {
            var userId = GetSelectedUserId();
            if (!userId.HasValue)
            {
                MessageBox.Show("Selecione um usuário para excluir.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var name = _grid.SelectedRows[0].Cells[1].Value.ToString();
            if (MessageBox.Show($"Excluir o usuário \"{name}\"?\nEsta ação não pode ser desfeita.",
                _config.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _userService.Delete(userId.Value);
                _db.InsertLog(null, "event", null, $"Usuário excluído: {name}");
                RefreshList();
            }
        }

        private void ToggleUser()
        {
            var userId = GetSelectedUserId();
            if (!userId.HasValue)
            {
                MessageBox.Show("Selecione um usuário.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var active = _grid.SelectedRows[0].Cells[4].Value.ToString() == "Sim";
            _userService.ToggleActive(userId.Value, !active);
            _db.InsertLog(null, "event", null, $"Usuário {(active ? "desativado" : "ativado")}: ID {userId}");
            RefreshList();
        }

        private Form CreateUserDialog(int? userId)
        {
            var form = new Form
            {
                Text = userId.HasValue ? "Editar Usuário" : "Novo Usuário",
                Size = new Size(500, 480),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = Color.FromArgb(22, 22, 45),
                MaximizeBox = false,
                MinimizeBox = false
            };

            var y = 15;
            var txtFullName = AddField(form, "Nome Completo:", ref y);
            var txtUsername = AddField(form, "Nome de Usuário:", ref y);
            var txtPin = AddField(form, "PIN (4-8 dígitos):", ref y);
            txtPin.MaxLength = 8;
            txtPin.PasswordChar = '●';

            var lblProfile = new Label
            {
                Text = "Perfil de Acesso:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 25;
            var cmbProfile = new ComboBox
            {
                Location = new Point(20, y),
                Size = new Size(440, 30),
                Font = new Font("Segoe UI", 11),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White
            };
            y += 45;

            cmbProfile.Items.Add(new ComboItem<int?>(null, "Nenhum"));
            try
            {
                using var conn = _db.GetConnection();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, name FROM access_profiles ORDER BY name";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    cmbProfile.Items.Add(new ComboItem<int?>(reader.GetInt32(0), reader.GetString(1)));
            }
            catch { }

            var lblPhoto = new Label
            {
                Text = "Foto (opcional):",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 25;
            var txtPhoto = new TextBox
            {
                Location = new Point(20, y),
                Size = new Size(350, 30),
                Font = new Font("Segoe UI", 10),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true
            };
            var btnBrowse = new Button
            {
                Text = "Procurar...",
                Location = new Point(380, y),
                Size = new Size(80, 30),
                Font = new Font("Segoe UI", 9),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "Imagens|*.jpg;*.jpeg;*.png" };
                if (ofd.ShowDialog() == DialogResult.OK)
                    txtPhoto.Text = ofd.FileName;
            };
            y += 45;

            if (userId.HasValue)
            {
                var user = _userService.GetById(userId.Value);
                if (user != null)
                {
                    txtFullName.Text = user.FullName;
                    txtUsername.Text = user.Username;
                    txtPhoto.Text = user.PhotoPath ?? "";
                    foreach (ComboItem<int?> item in cmbProfile.Items)
                    {
                        if (item.Value == user.ProfileId)
                        {
                            cmbProfile.SelectedItem = item;
                            break;
                        }
                    }
                }
            }

            var btnSave = new Button
            {
                Text = "Salvar",
                Location = new Point(150, y + 10),
                Size = new Size(200, 40),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(124, 58, 237),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtFullName.Text) || string.IsNullOrWhiteSpace(txtUsername.Text))
                {
                    MessageBox.Show("Preencha Nome e Usuário.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!userId.HasValue && (txtPin.Text.Length < 4 || txtPin.Text.Length > 8))
                {
                    MessageBox.Show("PIN deve ter entre 4 e 8 dígitos.", _config.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var profileId = (cmbProfile.SelectedItem as ComboItem<int?>)?.Value;
                var photoPath = string.IsNullOrWhiteSpace(txtPhoto.Text) ? null : txtPhoto.Text;

                if (userId.HasValue)
                {
                    _userService.Update(userId.Value, txtFullName.Text.Trim(), txtUsername.Text.Trim(),
                        profileId, true, photoPath);
                    if (!string.IsNullOrWhiteSpace(txtPin.Text))
                    {
                        var hash = PasswordHash.HashPassword(txtPin.Text);
                        using var conn = _db.GetConnection();
                        var cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE users SET pin_hash = @h WHERE id = @id";
                        cmd.Parameters.AddWithValue("@h", hash);
                        cmd.Parameters.AddWithValue("@id", userId.Value);
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    var hash = PasswordHash.HashPassword(txtPin.Text);
                    using var conn = _db.GetConnection();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"INSERT INTO users (full_name, username, pin_hash, profile_id, photo_path, is_active)
                                       VALUES (@fn, @un, @ph, @pi, @pp, 1)";
                    cmd.Parameters.AddWithValue("@fn", txtFullName.Text.Trim());
                    cmd.Parameters.AddWithValue("@un", txtUsername.Text.Trim());
                    cmd.Parameters.AddWithValue("@ph", hash);
                    cmd.Parameters.AddWithValue("@pi", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@pp", (object?)photoPath ?? DBNull.Value);
                    cmd.ExecuteNonQuery();
                }
                _db.InsertLog(null, "event", null, $"Usuário {(userId.HasValue ? "editado" : "criado")}: {txtFullName.Text}");
                form.DialogResult = DialogResult.OK;
                form.Close();
            };

            form.Controls.AddRange(new Control[] { txtFullName, txtUsername, txtPin, lblProfile, cmbProfile,
                lblPhoto, txtPhoto, btnBrowse, btnSave });
            return form;
        }

        private TextBox AddField(Form form, string label, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, y),
                AutoSize = true
            };
            y += 25;
            var txt = new TextBox
            {
                Location = new Point(20, y),
                Size = new Size(440, 30),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            y += 45;
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
