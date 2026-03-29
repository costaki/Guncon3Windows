using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Guncon3Console.Calibration
{
    internal sealed class RectCalibEditorForm : Form
    {
        private readonly string _path;
        private RectCalib _model;
        private bool _dirty;

        private NumericUpDown _rawMinX;
        private NumericUpDown _rawMaxX;
        private NumericUpDown _rawMinY;
        private NumericUpDown _rawMaxY;
        private NumericUpDown _screenW;
        private NumericUpDown _screenH;
        private CheckBox _invertY;

        public RectCalibEditorForm(string calibrationPath)
        {
            if (string.IsNullOrWhiteSpace(calibrationPath))
                throw new ArgumentException("Calibration path is required.", nameof(calibrationPath));

            _path = calibrationPath;

            Text = "RectCalib Editor";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 420);
            MinimumSize = new Size(520, 420);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                Padding = new Padding(10)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            Controls.Add(root);

            var warning = new Label
            {
                Text = "Warning: calibration should not generally be edited manually. Use normal calibration whenever possible.",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.DarkRed
            };
            root.Controls.Add(warning, 0, 0);
            root.SetColumnSpan(warning, 2);

            _rawMinX = AddNumber(root, 1, "RawMinX", -32768, 32767, 0);
            _rawMaxX = AddNumber(root, 2, "RawMaxX", -32768, 32767, 0);
            _rawMinY = AddNumber(root, 3, "RawMinY", -32768, 32767, 0);
            _rawMaxY = AddNumber(root, 4, "RawMaxY", -32768, 32767, 0);
            _screenW = AddNumber(root, 5, "ScreenW", 1, 16384, 0);
            _screenH = AddNumber(root, 6, "ScreenH", 1, 16384, 0);

            var lblInv = new Label { Text = "InvertY", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            _invertY = new CheckBox { Dock = DockStyle.Left };
            _invertY.CheckedChanged += (s, e) => _dirty = true;
            root.Controls.Add(lblInv, 0, 7);
            root.Controls.Add(_invertY, 1, 7);

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false
            };
            root.Controls.Add(buttonRow, 0, 9);
            root.SetColumnSpan(buttonRow, 2);

            var btnSave = new Button { Text = "Save", AutoSize = true };
            btnSave.Click += (s, e) => Save();
            buttonRow.Controls.Add(btnSave);

            var btnClose = new Button { Text = "Close", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            buttonRow.Controls.Add(btnClose);

            Load += (s, e) => LoadModel();
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
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            var num = new NumericUpDown { Dock = DockStyle.Fill, Minimum = min, Maximum = max, DecimalPlaces = decimals };
            num.ValueChanged += (s, e) =>
            {
                var f = num.FindForm() as RectCalibEditorForm;
                if (f != null) f._dirty = true;
            };

            root.Controls.Add(lbl, 0, row);
            root.Controls.Add(num, 1, row);
            return num;
        }

        private void LoadModel()
        {
            _model = RectCalib.Load(_path) ?? new RectCalib(_path);

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
                    _model = new RectCalib(_path);

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
