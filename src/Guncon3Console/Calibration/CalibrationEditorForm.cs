using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Guncon3Console.Calibration
{
    internal sealed class CalibrationEditorForm : Form
    {
        private readonly string _path;
        private GunCalibration _model;
        private bool _dirty;

        private NumericUpDown _rawMinX;
        private NumericUpDown _rawMaxX;
        private NumericUpDown _rawMinY;
        private NumericUpDown _rawMaxY;
        private NumericUpDown _screenW;
        private NumericUpDown _screenH;
        private CheckBox _invertY;

        public CalibrationEditorForm(string calibrationPath)
        {
            if (string.IsNullOrWhiteSpace(calibrationPath))
                throw new ArgumentException("Calibration path is required.", nameof(calibrationPath));

            _path = calibrationPath;

            Text = "Manual Calibration Editor";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(620, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(8),
                AutoSize = true
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var warning = new Label
            {
                Text = "Warning: calibration should not generally be edited manually. Use normal calibration whenever possible.",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.DarkRed,
                Font = new Font(Font.FontFamily, Font.Size + 2.0f, FontStyle.Bold)
            };
            root.Controls.Add(warning, 0, 0);

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(pathPanel, 0, 1);

            var pathLabel = new Label { Text = "File:", AutoSize = true, Anchor = AnchorStyles.Left };
            var pathBox = new TextBox
            {
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Text = _path,
                BorderStyle = BorderStyle.FixedSingle,
                Multiline = true,
                Height = 40,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true
            };
            pathPanel.Controls.Add(pathLabel, 0, 0);
            pathPanel.Controls.Add(pathBox, 1, 0);

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                AutoSize = true
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(fields, 0, 2);

            _rawMinX = AddNumber(fields, 0, "RawMinX", -32768, 32767, 0);
            _rawMaxX = AddNumber(fields, 1, "RawMaxX", -32768, 32767, 0);
            _rawMinY = AddNumber(fields, 2, "RawMinY", -32768, 32767, 0);
            _rawMaxY = AddNumber(fields, 3, "RawMaxY", -32768, 32767, 0);
            _screenW = AddNumber(fields, 4, "ScreenW", 1, 16384, 0);
            _screenH = AddNumber(fields, 5, "ScreenH", 1, 16384, 0);

            var lblInv = new Label { Text = "InvertY", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _invertY = new CheckBox { Dock = DockStyle.Left };
            _invertY.CheckedChanged += (s, e) => _dirty = true;
            fields.Controls.Add(lblInv, 0, 6);
            fields.Controls.Add(_invertY, 1, 6);

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            root.Controls.Add(buttonRow, 0, 3);

            var btnSave = new Button { Text = "Save", AutoSize = true };
            btnSave.Click += (s, e) => Save();
            buttonRow.Controls.Add(btnSave);

            var btnClose = new Button { Text = "Close", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            buttonRow.Controls.Add(btnClose);

            Load += (s, e) =>
            {
                // Encourage wrapping instead of horizontal cutoff.
                warning.MaximumSize = new Size(root.ClientSize.Width - root.Padding.Horizontal, 0);
                LoadModel();
            };
            FormClosing += (s, e) =>
            {
                if (!_dirty)
                    return;

                var r = MessageBox.Show(this, "Save changes?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }

                if (r == DialogResult.Yes)
                    e.Cancel = !Save();
            };
        }

        private static NumericUpDown AddNumber(TableLayoutPanel root, int row, string label, decimal min, decimal max, int decimals)
        {
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, Anchor = AnchorStyles.Left };
            var num = new NumericUpDown { Dock = DockStyle.Left, Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 140 };
            num.ValueChanged += (s, e) =>
            {
                var f = num.FindForm() as CalibrationEditorForm;
                if (f != null) f._dirty = true;
            };

            root.Controls.Add(lbl, 0, row);
            root.Controls.Add(num, 1, row);
            return num;
        }

        private void LoadModel()
        {
            _model = GunCalibration.Load(_path) ?? new GunCalibration(_path);

            _rawMinX.Value = (decimal)_model.RawMinX;
            _rawMaxX.Value = (decimal)_model.RawMaxX;
            _rawMinY.Value = (decimal)_model.RawMinY;
            _rawMaxY.Value = (decimal)_model.RawMaxY;
            _screenW.Value = _model.ScreenW;
            _screenH.Value = _model.ScreenH;
            _invertY.Checked = _model.InvertY;

            _dirty = false;
        }

        private bool Save()
        {
            try
            {
                if (_model == null)
                    _model = new GunCalibration(_path);

                _model.RawMinX = (double)_rawMinX.Value;
                _model.RawMaxX = (double)_rawMaxX.Value;
                _model.RawMinY = (double)_rawMinY.Value;
                _model.RawMaxY = (double)_rawMaxY.Value;
                _model.ScreenW = (int)_screenW.Value;
                _model.ScreenH = (int)_screenH.Value;
                _model.InvertY = _invertY.Checked;

                if (!_model.IsValid())
                {
                    MessageBox.Show(this, "Calibration values are not valid (min/max or screen size).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                // Ensure target directory exists
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _model.Save();
                _dirty = false;
                MessageBox.Show(this, "Calibration saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
