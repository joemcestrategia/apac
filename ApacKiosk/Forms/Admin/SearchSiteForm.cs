using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;

namespace ApacKiosk.Forms.Admin
{
    public class SearchSiteForm : Form
    {
        private TextBox _txtSearch;
        private Button _btnSearch;
        private DataGridView _grid;
        private Button _btnAdd;
        private Label _lblStatus;
        private static readonly HttpClient _httpClient = new HttpClient();

        public string? SelectedDomain { get; private set; }

        public SearchSiteForm()
        {
            Text = "Pesquisar Site na Web";
            Size = new Size(600, 450);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.FromArgb(22, 22, 45);
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var lbl = new Label
            {
                Text = "Digite o nome da faculdade ou site para pesquisar:",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 220),
                Location = new Point(20, 20),
                AutoSize = true
            };

            _txtSearch = new TextBox
            {
                Location = new Point(20, 50),
                Size = new Size(430, 28),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) _btnSearch.PerformClick(); };

            _btnSearch = new Button
            {
                Text = "Pesquisar",
                Location = new Point(460, 50),
                Size = new Size(100, 28),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(59, 130, 246),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnSearch.FlatAppearance.BorderSize = 0;
            _btnSearch.Click += async (s, e) => await SearchAsync();

            _grid = new DataGridView
            {
                Location = new Point(20, 100),
                Size = new Size(540, 230),
                BackgroundColor = Color.FromArgb(26, 26, 46),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(50, 50, 80),
                BorderStyle = BorderStyle.None,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                MultiSelect = false
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 35, 60);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(167, 139, 250);
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(26, 26, 46);
            _grid.DefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(124, 58, 237);
            
            _grid.Columns.Add("Title", "Título / Descrição");
            _grid.Columns.Add("Domain", "Domínio Encontrado");
            _grid.Columns[0].Width = 300;
            _grid.Columns[1].Width = 220;
            _grid.CellDoubleClick += (s, e) => _btnAdd.PerformClick();

            _lblStatus = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(180, 180, 200),
                Location = new Point(20, 340),
                AutoSize = true
            };

            _btnAdd = new Button
            {
                Text = "Adicionar Selecionado",
                Location = new Point(360, 350),
                Size = new Size(200, 35),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnAdd.FlatAppearance.BorderSize = 0;
            _btnAdd.Click += (s, e) =>
            {
                if (_grid.SelectedRows.Count > 0)
                {
                    SelectedDomain = _grid.SelectedRows[0].Cells[1].Value.ToString();
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };

            Controls.AddRange(new Control[] { lbl, _txtSearch, _btnSearch, _grid, _lblStatus, _btnAdd });
        }

        private async Task SearchAsync()
        {
            var query = _txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            _grid.Rows.Clear();
            _lblStatus.Text = "Pesquisando...";
            _btnSearch.Enabled = false;

            try
            {
                var url = $"https://autocomplete.clearbit.com/v1/companies/suggest?query={Uri.EscapeDataString(query)}";
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                var response = await _httpClient.GetStringAsync(url);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var results = JsonSerializer.Deserialize<List<ClearbitResult>>(response, options);

                if (results == null || results.Count == 0)
                {
                    _lblStatus.Text = "Nenhum resultado encontrado.";
                }
                else
                {
                    foreach (var res in results)
                    {
                        if (!string.IsNullOrEmpty(res.Domain))
                        {
                            _grid.Rows.Add(res.Name, res.Domain);
                        }
                    }
                    _lblStatus.Text = $"{_grid.Rows.Count} resultados encontrados.";
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Erro ao pesquisar: " + ex.Message;
            }
            finally
            {
                _btnSearch.Enabled = true;
            }
        }

        public class ClearbitResult
        {
            public string? Name { get; set; }
            public string? Domain { get; set; }
            public string? Logo { get; set; }
        }
    }
}
