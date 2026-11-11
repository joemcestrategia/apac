using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ApacKiosk.Forms
{
    /// <summary>
    /// Diálogos customizados renderizados como overlay dentro da janela pai.
    /// Substitui MessageBox.Show() e InputBox nativos do Windows para evitar
    /// que fiquem ocultos atrás de janelas TopMost.
    /// </summary>
    public static class KioskDialog
    {
        // Cores do tema
        private static readonly Color OverlayColor = Color.FromArgb(180, 0, 0, 0);
        private static readonly Color CardBg = Color.FromArgb(22, 22, 45);
        private static readonly Color CardBorder = Color.FromArgb(60, 60, 100);
        private static readonly Color TextColor = Color.White;
        private static readonly Color SubtextColor = Color.FromArgb(180, 180, 200);
        private static readonly Color InputBg = Color.FromArgb(26, 26, 46);
        private static readonly Color AccentColor = Color.FromArgb(124, 58, 237);
        private static readonly Color DangerColor = Color.FromArgb(200, 50, 50);
        private static readonly Color WarningColor = Color.FromArgb(245, 158, 11);
        private static readonly Color InfoColor = Color.FromArgb(59, 130, 246);
        private static readonly Color SuccessColor = Color.FromArgb(16, 185, 129);
        private static readonly Color NeutralBtnColor = Color.FromArgb(42, 42, 70);

        private static Form FindParentForm(Control control)
        {
            if (control is Form f) return f;
            var found = control?.FindForm();
            if (found != null) return found;
            return Application.OpenForms.Count > 0 ? Application.OpenForms[Application.OpenForms.Count - 1] : null;
        }

        /// <summary>
        /// Substituto para MessageBox.Show() com ícone. Mostra um diálogo de alerta dentro do formulário pai.
        /// </summary>
        public static DialogResult ShowMessage(string message, string title,
            MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon icon = MessageBoxIcon.None,
            Control parent = null)
        {
            var parentForm = FindParentForm(parent);
            if (parentForm == null || parentForm.IsDisposed)
            {
                // Fallback para MessageBox nativo se não houver formulário pai
                return MessageBox.Show(message, title, buttons, icon);
            }

            DialogResult result = DialogResult.None;

            if (parentForm.InvokeRequired)
            {
                parentForm.Invoke(new Action(() => result = ShowMessageInternal(parentForm, message, title, buttons, icon)));
            }
            else
            {
                result = ShowMessageInternal(parentForm, message, title, buttons, icon);
            }

            return result;
        }

        private static DialogResult ShowMessageInternal(Form parentForm, string message, string title,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            DialogResult result = DialogResult.None;

            // Painel de overlay escuro
            var overlay = CreateOverlay(parentForm);

            // Card central
            var card = CreateCard(420, 0); // altura será calculada

            // Ícone
            var iconText = GetIconText(icon);
            var iconColor = GetIconColor(icon);
            var iconLabel = new Label
            {
                Text = iconText,
                Font = new Font("Segoe UI", 32),
                ForeColor = iconColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(420, 50),
                Location = new Point(0, 20),
                BackColor = Color.Transparent
            };

            // Título
            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 30),
                Location = new Point(20, 75),
                BackColor = Color.Transparent
            };

            // Mensagem
            var msgLabel = new Label
            {
                Text = message,
                Font = new Font("Segoe UI", 11),
                ForeColor = SubtextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                MaximumSize = new Size(380, 200),
                AutoSize = true,
                Location = new Point(20, 115),
                BackColor = Color.Transparent
            };

            card.Controls.AddRange(new Control[] { iconLabel, titleLabel, msgLabel });

            // Calcula posição da mensagem e botões
            card.SuspendLayout();
            msgLabel.Location = new Point((420 - msgLabel.PreferredWidth) / 2, 115);

            int buttonY = msgLabel.Bottom + 25;

            // Botões
            var btnPanel = new Panel
            {
                Size = new Size(420, 50),
                Location = new Point(0, buttonY),
                BackColor = Color.Transparent
            };

            switch (buttons)
            {
                case MessageBoxButtons.OK:
                    var btnOk = CreateDialogButton("OK", AccentColor, 160, 0, 100);
                    btnOk.Click += (s, e) => { result = DialogResult.OK; overlay.Dispose(); };
                    btnPanel.Controls.Add(btnOk);
                    break;

                case MessageBoxButtons.YesNo:
                    var btnYes = CreateDialogButton("Sim", AccentColor, 100, 0, 100);
                    var btnNo = CreateDialogButton("Não", NeutralBtnColor, 220, 0, 100);
                    btnYes.Click += (s, e) => { result = DialogResult.Yes; overlay.Dispose(); };
                    btnNo.Click += (s, e) => { result = DialogResult.No; overlay.Dispose(); };
                    btnPanel.Controls.AddRange(new Control[] { btnYes, btnNo });
                    break;

                case MessageBoxButtons.OKCancel:
                    var btnOk2 = CreateDialogButton("OK", AccentColor, 100, 0, 100);
                    var btnCancel2 = CreateDialogButton("Cancelar", NeutralBtnColor, 220, 0, 100);
                    btnOk2.Click += (s, e) => { result = DialogResult.OK; overlay.Dispose(); };
                    btnCancel2.Click += (s, e) => { result = DialogResult.Cancel; overlay.Dispose(); };
                    btnPanel.Controls.AddRange(new Control[] { btnOk2, btnCancel2 });
                    break;

                case MessageBoxButtons.YesNoCancel:
                    var btnY = CreateDialogButton("Sim", AccentColor, 60, 0, 90);
                    var btnN = CreateDialogButton("Não", DangerColor, 165, 0, 90);
                    var btnC = CreateDialogButton("Cancelar", NeutralBtnColor, 270, 0, 90);
                    btnY.Click += (s, e) => { result = DialogResult.Yes; overlay.Dispose(); };
                    btnN.Click += (s, e) => { result = DialogResult.No; overlay.Dispose(); };
                    btnC.Click += (s, e) => { result = DialogResult.Cancel; overlay.Dispose(); };
                    btnPanel.Controls.AddRange(new Control[] { btnY, btnN, btnC });
                    break;
            }

            card.Controls.Add(btnPanel);

            int cardHeight = buttonY + 70;
            card.Size = new Size(420, cardHeight);
            card.Location = new Point(
                (overlay.Width - 420) / 2,
                (overlay.Height - cardHeight) / 2
            );

            ApplyRoundedCorners(card, 16);

            card.ResumeLayout();
            overlay.Controls.Add(card);
            card.BringToFront();

            parentForm.Controls.Add(overlay);
            overlay.BringToFront();
            overlay.Focus();

            // Processamento modal de eventos
            while (result == DialogResult.None && !overlay.IsDisposed)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }

            if (!overlay.IsDisposed)
            {
                parentForm.Controls.Remove(overlay);
                overlay.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Substituto para InputBox. Mostra campo de entrada dentro do formulário pai.
        /// Retorna string vazia se cancelado.
        /// </summary>
        public static string ShowInput(string prompt, string title, string defaultValue = "",
            bool isPassword = false, Control parent = null)
        {
            var parentForm = FindParentForm(parent);
            if (parentForm == null || parentForm.IsDisposed)
                return "";

            string resultText = null;

            if (parentForm.InvokeRequired)
            {
                parentForm.Invoke(new Action(() => resultText = ShowInputInternal(parentForm, prompt, title, defaultValue, isPassword)));
            }
            else
            {
                resultText = ShowInputInternal(parentForm, prompt, title, defaultValue, isPassword);
            }

            return resultText ?? "";
        }

        private static string ShowInputInternal(Form parentForm, string prompt, string title,
            string defaultValue, bool isPassword)
        {
            string resultText = null;
            bool done = false;

            var overlay = CreateOverlay(parentForm);

            var card = CreateCard(450, 260);

            // Título
            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(410, 35),
                Location = new Point(20, 20),
                BackColor = Color.Transparent
            };

            // Prompt
            var promptLabel = new Label
            {
                Text = prompt,
                Font = new Font("Segoe UI", 11),
                ForeColor = SubtextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                MaximumSize = new Size(410, 60),
                AutoSize = true,
                Location = new Point(20, 60),
                BackColor = Color.Transparent
            };

            // Input
            var textBox = new TextBox
            {
                Text = defaultValue,
                Font = new Font("Segoe UI", 13),
                Size = new Size(390, 36),
                Location = new Point(30, 110),
                BackColor = InputBg,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle
            };
            if (isPassword) textBox.PasswordChar = '●';

            // Botões
            var btnOk = CreateDialogButton("Confirmar", AccentColor, 100, 170, 120);
            var btnCancel = CreateDialogButton("Cancelar", NeutralBtnColor, 230, 170, 120);

            btnOk.Click += (s, e) =>
            {
                resultText = textBox.Text;
                done = true;
            };
            btnCancel.Click += (s, e) =>
            {
                resultText = null;
                done = true;
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    resultText = textBox.Text;
                    done = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    resultText = null;
                    done = true;
                    e.SuppressKeyPress = true;
                }
            };

            card.Controls.AddRange(new Control[] { titleLabel, promptLabel, textBox, btnOk, btnCancel });

            card.Location = new Point(
                (overlay.Width - 450) / 2,
                (overlay.Height - 260) / 2
            );
            ApplyRoundedCorners(card, 16);

            overlay.Controls.Add(card);
            card.BringToFront();

            parentForm.Controls.Add(overlay);
            overlay.BringToFront();
            textBox.Focus();

            while (!done && !overlay.IsDisposed)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }

            if (!overlay.IsDisposed)
            {
                parentForm.Controls.Remove(overlay);
                overlay.Dispose();
            }

            return resultText ?? "";
        }

        /// <summary>
        /// Mostra um diálogo de senha dentro do formulário pai.
        /// Retorna (DialogResult, senhaDigitada).
        /// </summary>
        public static (DialogResult result, string password) ShowPasswordPrompt(
            string prompt, string title, Control parent = null)
        {
            var parentForm = FindParentForm(parent);
            if (parentForm == null || parentForm.IsDisposed)
                return (DialogResult.Cancel, "");

            DialogResult dialogResult = DialogResult.Cancel;
            string password = "";

            if (parentForm.InvokeRequired)
            {
                parentForm.Invoke(new Action(() =>
                {
                    var r = ShowPasswordPromptInternal(parentForm, prompt, title);
                    dialogResult = r.result;
                    password = r.password;
                }));
            }
            else
            {
                var r = ShowPasswordPromptInternal(parentForm, prompt, title);
                dialogResult = r.result;
                password = r.password;
            }

            return (dialogResult, password);
        }

        private static (DialogResult result, string password) ShowPasswordPromptInternal(
            Form parentForm, string prompt, string title)
        {
            DialogResult result = DialogResult.None;
            string password = "";
            bool done = false;

            var overlay = CreateOverlay(parentForm);

            var card = CreateCard(420, 280);

            // Ícone de cadeado
            var iconLabel = new Label
            {
                Text = "🔒",
                Font = new Font("Segoe UI", 28),
                ForeColor = AccentColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(420, 45),
                Location = new Point(0, 15),
                BackColor = Color.Transparent
            };

            // Título
            var titleLabel = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = TextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 30),
                Location = new Point(20, 60),
                BackColor = Color.Transparent
            };

            // Prompt
            var promptLabel = new Label
            {
                Text = prompt,
                Font = new Font("Segoe UI", 10),
                ForeColor = SubtextColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(380, 25),
                Location = new Point(20, 95),
                BackColor = Color.Transparent
            };

            // Campo de senha
            var textBox = new TextBox
            {
                Font = new Font("Segoe UI", 13),
                Size = new Size(360, 36),
                Location = new Point(30, 130),
                BackColor = InputBg,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle,
                PasswordChar = '●'
            };

            // Botões
            var btnOk = CreateDialogButton("Confirmar", AccentColor, 80, 190, 120);
            var btnCancel = CreateDialogButton("Cancelar", NeutralBtnColor, 220, 190, 120);

            btnOk.Click += (s, e) =>
            {
                result = DialogResult.OK;
                password = textBox.Text;
                done = true;
            };
            btnCancel.Click += (s, e) =>
            {
                result = DialogResult.Cancel;
                password = "";
                done = true;
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    result = DialogResult.OK;
                    password = textBox.Text;
                    done = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    result = DialogResult.Cancel;
                    password = "";
                    done = true;
                    e.SuppressKeyPress = true;
                }
            };

            card.Controls.AddRange(new Control[] { iconLabel, titleLabel, promptLabel, textBox, btnOk, btnCancel });

            card.Location = new Point(
                (overlay.Width - 420) / 2,
                (overlay.Height - 280) / 2
            );
            ApplyRoundedCorners(card, 16);

            overlay.Controls.Add(card);
            card.BringToFront();

            parentForm.Controls.Add(overlay);
            overlay.BringToFront();
            textBox.Focus();

            while (!done && !overlay.IsDisposed)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }

            if (!overlay.IsDisposed)
            {
                parentForm.Controls.Remove(overlay);
                overlay.Dispose();
            }

            return (result, password);
        }

        #region Helpers

        private static Panel CreateOverlay(Form parentForm)
        {
            var overlay = new Panel
            {
                Size = parentForm.ClientSize,
                Location = new Point(0, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(1, 1, 1)
            };

            // Pintar o overlay com transparência
            overlay.Paint += (s, e) =>
            {
                using var brush = new SolidBrush(OverlayColor);
                e.Graphics.FillRectangle(brush, overlay.ClientRectangle);
            };

            // Impedir clique "passar" pelo overlay
            overlay.Click += (s, e) => { };

            return overlay;
        }

        private static Panel CreateCard(int width, int height)
        {
            var card = new Panel
            {
                Size = new Size(width, height),
                BackColor = CardBg,
                Padding = new Padding(0)
            };

            card.Paint += (s, e) =>
            {
                using var pen = new Pen(CardBorder, 1);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };

            return card;
        }

        private static Button CreateDialogButton(string text, Color bgColor, int x, int y, int width)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Size = new Size(width, 38),
                Location = new Point(x, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgColor,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bgColor, 0.15f);
            return btn;
        }

        private static void ApplyRoundedCorners(Control control, int radius)
        {
            var path = new GraphicsPath();
            var r = radius * 2;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(control.Width - r, 0, r, r, 270, 90);
            path.AddArc(control.Width - r, control.Height - r, r, r, 0, 90);
            path.AddArc(0, control.Height - r, r, r, 90, 90);
            path.CloseFigure();
            control.Region = new Region(path);
        }

        private static string GetIconText(MessageBoxIcon icon)
        {
            return icon switch
            {
                MessageBoxIcon.Error => "✕",
                MessageBoxIcon.Warning => "⚠",
                MessageBoxIcon.Information => "ℹ",
                MessageBoxIcon.Question => "?",
                _ => ""
            };
        }

        private static Color GetIconColor(MessageBoxIcon icon)
        {
            return icon switch
            {
                MessageBoxIcon.Error => DangerColor,
                MessageBoxIcon.Warning => WarningColor,
                MessageBoxIcon.Information => InfoColor,
                MessageBoxIcon.Question => AccentColor,
                _ => TextColor
            };
        }

        #endregion
    }
}
