using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Guncon3Console.TetherScript;
using Guncon3Console.Calibration;
using GunconUSB;

namespace Guncon3Console.Mapping
{
    internal sealed class MappingEditorForm : Form
    {
        private readonly string _p1Path;
        private readonly string _p2Path;
        private readonly bool _enableP2;

        private readonly GunconDevice _p1Device;
        private readonly GunconDevice _p2Device;

        private readonly DataGridView _gridP1;
        private readonly DataGridView _gridP2;

        private readonly TabControl _tabs;

        private GunMappingModel _modelP1;
        private GunMappingModel _modelP2;

        private bool _dirty;

        public MappingEditorForm(string player1Path, string player2Path, bool enablePlayer2, GunconDevice player1Device = null, GunconDevice player2Device = null)
        {
            _p1Path = player1Path ?? throw new ArgumentNullException(nameof(player1Path));
            _p2Path = player2Path;
            _enableP2 = enablePlayer2;
            _p1Device = player1Device;
            _p2Device = player2Device;

            Text = "Guncon3 Mapping Editor";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 700);
            MinimumSize = new Size(800, 600);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs = tabs;
            root.Controls.Add(_tabs, 0, 0);

            _gridP1 = CreateGrid();
            var p1Page = new TabPage("Player 1");
            p1Page.Controls.Add(CreatePlayerPanel(player: 1, _gridP1));
            _tabs.TabPages.Add(p1Page);

            if (_enableP2)
            {
                _gridP2 = CreateGrid();
                var p2Page = new TabPage("Player 2");
                p2Page.Controls.Add(CreatePlayerPanel(player: 2, _gridP2));
                _tabs.TabPages.Add(p2Page);
            }

            Load += (s, e) => LoadAll();
            FormClosing += (s, e) =>
            {
                if (!e.Cancel)
                    e.Cancel = !PromptSaveIfDirty();
            };
        }

        private Control CreatePlayerPanel(int player, DataGridView grid)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            panel.Controls.Add(grid, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = true
            };
            panel.Controls.Add(buttons, 0, 1);

            // Player-specific buttons first
            var btnCalibrate = new Button { Text = player == 1 ? "Calibrate P1" : "Calibrate P2", AutoSize = true };
            btnCalibrate.Click += (s, e) => CalibratePlayer(player);
            buttons.Controls.Add(btnCalibrate);

            var btnManual = new Button { Text = "Manual Edit Calibration", AutoSize = true };
            btnManual.Click += (s, e) => ManualEditCalibration(player);
            buttons.Controls.Add(btnManual);

            if (_enableP2)
            {
                if (player == 1)
                {
                    var btnCopyTo = new Button { Text = "Copy P1 → P2", AutoSize = true };
                    btnCopyTo.Click += (s, e) =>
                    {
                        if (!Confirm("Overwrite Player 2 mappings with Player 1 mappings?"))
                            return;
                        CopyGrid(_gridP1, _gridP2);
                    };
                    buttons.Controls.Add(btnCopyTo);
                }
                else
                {
                    var btnCopyTo = new Button { Text = "Copy P2 → P1", AutoSize = true };
                    btnCopyTo.Click += (s, e) =>
                    {
                        if (!Confirm("Overwrite Player 1 mappings with Player 2 mappings?"))
                            return;
                        CopyGrid(_gridP2, _gridP1);
                    };
                    buttons.Controls.Add(btnCopyTo);
                }

                if (player == 2)
                {
                    var btnCalibBoth = new Button { Text = "Calibrate Both", AutoSize = true };
                    btnCalibBoth.Click += (s, e) => CalibrateBoth();
                    buttons.Controls.Add(btnCalibBoth);
                }
            }

            // Common actions last
            var btnClearAll = new Button { Text = "Clear All", AutoSize = true };
            btnClearAll.Click += (s, e) =>
            {
                var label = player == 1 ? "Player 1" : "Player 2";
                if (Confirm($"Clear all mappings for {label}?"))
                {
                    ClearGrid(grid);
                    _dirty = true;
                }
            };
            buttons.Controls.Add(btnClearAll);

            var btnReload = new Button { Text = "Reload", AutoSize = true };
            btnReload.Click += (s, e) =>
            {
                if (Confirm("Reload mappings from disk and discard unsaved changes?"))
                    LoadAll();
            };
            buttons.Controls.Add(btnReload);

            var btnSave = new Button { Text = "Save", AutoSize = true };
            btnSave.Click += (s, e) => SaveAll(promptCloseAfterSave: true);
            buttons.Controls.Add(btnSave);

            var btnClose = new Button { Text = "Close", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            buttons.Controls.Add(btnClose);

            return panel;
        }

        private void CalibratePlayer(int player)
        {
            if (player == 1)
            {
                if (_p1Device == null)
                {
                    MessageBox.Show(this, "Player 1 device is not available.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!Confirm("Start calibration for Player 1?"))
                    return;

                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _enableP2 ? RectCalib.Player1FileName : RectCalib.DefaultFileName);
                using (var w = new Guncon3Console.Calibration.CalibrationWindow(path, "Calibrating: Player 1 / Gun 1", _p1Device))
                    w.ShowDialog(this);
                return;
            }

            if (player == 2)
            {
                if (!_enableP2 || _p2Device == null)
                {
                    MessageBox.Show(this, "Player 2 device is not available.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (!Confirm("Start calibration for Player 2?"))
                    return;

                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RectCalib.Player2FileName);
                using (var w = new Guncon3Console.Calibration.CalibrationWindow(path, "Calibrating: Player 2 / Gun 2", _p2Device))
                    w.ShowDialog(this);
                return;
            }
        }

        private void CalibrateBoth()
        {
            if (!_enableP2)
                return;

            if (_p1Device == null || _p2Device == null)
            {
                MessageBox.Show(this, "Both devices are required for dual calibration.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Confirm("Start calibration for Player 1 and Player 2?"))
                return;

            CalibratePlayer(1);
            CalibratePlayer(2);
        }

        private static bool Confirm(string message)
        {
            return MessageBox.Show(message, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private bool PromptSaveIfDirty()
        {
            if (!_dirty)
                return true;

            var r = MessageBox.Show(this, "Save changes?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel)
                return false;
            if (r == DialogResult.No)
                return true;

            return SaveAll(promptCloseAfterSave: false);
        }

        private static DataGridView CreateGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            grid.DataError += (s, e) =>
            {
                // Ignore invalid persisted values (e.g. older mapping files) instead of showing the default dialog.
                e.ThrowException = false;
            };

            var colBtn = new DataGridViewTextBoxColumn
            {
                Name = "GunButton",
                HeaderText = "GunButton",
                ReadOnly = true,
                FillWeight = 25
            };
            grid.Columns.Add(colBtn);

            var colMouse = new DataGridViewComboBoxColumn
            {
                Name = "Mouse",
                HeaderText = "Mouse",
                FlatStyle = FlatStyle.Flat,
                FillWeight = 25
            };
            colMouse.Items.Add("");
            colMouse.Items.AddRange(new object[] { "Left", "Right", "Middle" });
            grid.Columns.Add(colMouse);

            var colKey = new DataGridViewComboBoxColumn
            {
                Name = "Keyboard",
                HeaderText = "Keyboard (HidKeyCode)",
                FlatStyle = FlatStyle.Flat,
                FillWeight = 55
            };
            colKey.Items.Add("");
            colKey.Items.AddRange(Enum.GetNames(typeof(HidKeyCode)).Cast<object>().ToArray());
            grid.Columns.Add(colKey);

            var colClear = new DataGridViewButtonColumn
            {
                Name = "Clear",
                HeaderText = string.Empty,
                Text = "Clear",
                UseColumnTextForButtonValue = true,
                FillWeight = 20
            };
            grid.Columns.Add(colClear);

            grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0)
                    return;

                if (grid.Columns[e.ColumnIndex].Name == "Clear")
                {
                    var row = grid.Rows[e.RowIndex];
                    row.Cells["Mouse"].Value = string.Empty;
                    row.Cells["Keyboard"].Value = string.Empty;
                    UpdateRowEnabledState(grid, row);
                }
            };

            grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                var row = grid.Rows[e.RowIndex];
                UpdateRowEnabledState(grid, row);

                var f = grid.FindForm() as MappingEditorForm;
                if (f != null)
                    f._dirty = true;
            };

            grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (grid.IsCurrentCellDirty)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            return grid;
        }

        private void LoadAll()
        {
            _modelP1 = GunMappingStore.Load(_p1Path);
            LoadModelIntoGrid(_modelP1, _gridP1);

            if (_enableP2 && _gridP2 != null)
            {
                _modelP2 = GunMappingStore.Load(_p2Path);
                LoadModelIntoGrid(_modelP2, _gridP2);
            }

            _dirty = false;
        }

        private static void LoadModelIntoGrid(GunMappingModel model, DataGridView grid)
        {
            grid.Rows.Clear();

            var mouseColumn = (DataGridViewComboBoxColumn)grid.Columns["Mouse"];
            var keyColumn = (DataGridViewComboBoxColumn)grid.Columns["Keyboard"];

            foreach (GunButton b in Enum.GetValues(typeof(GunButton)))
            {
                var i = grid.Rows.Add();
                var row = grid.Rows[i];
                row.Cells["GunButton"].Value = b.ToString();

                model.Mouse.TryGetValue(b, out var mouse);
                model.Keyboard.TryGetValue(b, out var key);

                var m = (mouse ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(m) || !mouseColumn.Items.Contains(m))
                    m = string.Empty;

                var k = (key ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(k) && !keyColumn.Items.Contains(k))
                {
                    // Support older mapping files that persisted numeric HID usage IDs (e.g. "40")
                    // by converting them back to the enum name.
                    if (byte.TryParse(k, out var usage) && Enum.IsDefined(typeof(HidKeyCode), usage))
                        k = ((HidKeyCode)usage).ToString();
                }

                if (string.IsNullOrEmpty(k) || !keyColumn.Items.Contains(k))
                    k = string.Empty;

                row.Cells["Mouse"].Value = m;
                row.Cells["Keyboard"].Value = k;

                UpdateRowEnabledState(grid, row);
            }
        }

        private static void UpdateRowEnabledState(DataGridView grid, DataGridViewRow row)
        {
            var mouse = (row.Cells["Mouse"].Value as string) ?? string.Empty;
            var key = (row.Cells["Keyboard"].Value as string) ?? string.Empty;

            bool hasMouse = !string.IsNullOrWhiteSpace(mouse);
            bool hasKey = !string.IsNullOrWhiteSpace(key);

            // Mutual exclusion: if one side is set, disable the other.
            row.Cells["Keyboard"].ReadOnly = hasMouse;
            row.Cells["Mouse"].ReadOnly = hasKey;

            // Keep UI in sync if both are set somehow.
            if (hasMouse && hasKey)
            {
                row.Cells["Keyboard"].Value = string.Empty;
                row.Cells["Keyboard"].ReadOnly = true;
                row.Cells["Mouse"].ReadOnly = false;
            }

            // Ensure disabled combo looks disabled.
            var keyCell = row.Cells["Keyboard"];
            keyCell.Style.BackColor = keyCell.ReadOnly ? SystemColors.Control : SystemColors.Window;
            var mouseCell = row.Cells["Mouse"];
            mouseCell.Style.BackColor = mouseCell.ReadOnly ? SystemColors.Control : SystemColors.Window;
        }

        private static void SaveGridToModel(DataGridView grid, GunMappingModel model)
        {
            model.Mouse.Clear();
            model.Keyboard.Clear();

            foreach (DataGridViewRow row in grid.Rows)
            {
                var btnRaw = row.Cells["GunButton"].Value as string;
                if (string.IsNullOrWhiteSpace(btnRaw))
                    continue;

                if (!Enum.TryParse<GunButton>(btnRaw, out var b))
                    continue;

                var mouse = row.Cells["Mouse"].Value as string;
                var key = row.Cells["Keyboard"].Value as string;

                if (!string.IsNullOrWhiteSpace(mouse))
                    model.Mouse[b] = mouse.Trim();

                if (!string.IsNullOrWhiteSpace(key))
                {
                    var keyName = key.Trim();
                    if (Enum.TryParse<HidKeyCode>(keyName, ignoreCase: true, out var hk))
                        model.Keyboard[b] = ((byte)hk).ToString();
                }
            }
        }

        private void ClearAll()
        {
            var activeGrid = GetActiveGrid();
            if (activeGrid == null)
                return;

            ClearGrid(activeGrid);
            _dirty = true;
        }

        private DataGridView GetActiveGrid()
        {
            var page = _tabs?.SelectedTab;
            if (page == null)
                return null;
            return page.Controls.OfType<TableLayoutPanel>().SelectMany(p => p.Controls.OfType<DataGridView>()).FirstOrDefault();
        }

        private void ManualEditCalibration(int player)
        {
            var r = MessageBox.Show(this,
                "Warning: calibration should not generally be edited manually. Use normal calibration whenever possible.\n\nContinue?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (r != DialogResult.Yes)
                return;

            var path = (player == 1)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _enableP2 ? RectCalib.Player1FileName : RectCalib.DefaultFileName)
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RectCalib.Player2FileName);

            using (var w = new RectCalibEditorForm(path))
                w.ShowDialog(this);

            // Ensure running state picks up manual edits.
            try
            {
                if (player == 1 && Guncon3Console.Program.ProgramCalibration.TryRefresh(1)) { }
                else if (player == 2 && Guncon3Console.Program.ProgramCalibration.TryRefresh(2)) { }
            }
            catch { }
        }

        private static void ClearGrid(DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                row.Cells["Mouse"].Value = string.Empty;
                row.Cells["Keyboard"].Value = string.Empty;
                UpdateRowEnabledState(grid, row);
            }
        }

        private static void CopyGrid(DataGridView from, DataGridView to)
        {
            if (from == null || to == null)
                return;

            for (int i = 0; i < from.Rows.Count && i < to.Rows.Count; i++)
            {
                var rFrom = from.Rows[i];
                var rTo = to.Rows[i];

                rTo.Cells["Mouse"].Value = rFrom.Cells["Mouse"].Value;
                rTo.Cells["Keyboard"].Value = rFrom.Cells["Keyboard"].Value;
                UpdateRowEnabledState(to, rTo);
            }

            var f = to.FindForm() as MappingEditorForm;
            if (f != null)
                f._dirty = true;
        }

        private void SaveAll()
        {
            SaveAll(promptCloseAfterSave: false);
        }

        private bool SaveAll(bool promptCloseAfterSave)
        {
            try
            {
                var warnings = ValidateAll();
                if (!string.IsNullOrEmpty(warnings))
                {
                    var res = MessageBox.Show(this, warnings + "\n\nContinue saving?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (res != DialogResult.Yes)
                        return false;
                }

                SaveGridToModel(_gridP1, _modelP1);
                GunMappingStore.Save(_p1Path, _modelP1);

                if (_enableP2 && _gridP2 != null)
                {
                    SaveGridToModel(_gridP2, _modelP2);
                    GunMappingStore.Save(_p2Path, _modelP2);
                }

                _dirty = false;

                if (promptCloseAfterSave)
                {
                    var r = MessageBox.Show(this, "Mappings saved. Close this window?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes)
                        Close();
                }
                else
                {
                    MessageBox.Show(this, "Mappings saved.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private string ValidateAll()
        {
            var msg = string.Empty;
            msg += ValidateDuplicates(_gridP1, "Player 1");
            if (_enableP2 && _gridP2 != null)
                msg += ValidateDuplicates(_gridP2, "Player 2");
            return msg;
        }

        private static string ValidateDuplicates(DataGridView grid, string label)
        {
            var mouse = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);
            var keys = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in grid.Rows)
            {
                var btn = row.Cells["GunButton"].Value as string;
                if (string.IsNullOrWhiteSpace(btn))
                    continue;

                var m = (row.Cells["Mouse"].Value as string) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(m))
                {
                    if (!mouse.TryGetValue(m, out var l)) mouse[m] = l = new System.Collections.Generic.List<string>();
                    l.Add(btn);
                }

                var k = (row.Cells["Keyboard"].Value as string) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(k))
                {
                    if (!keys.TryGetValue(k, out var l)) keys[k] = l = new System.Collections.Generic.List<string>();
                    l.Add(btn);
                }
            }

            var lines = new System.Collections.Generic.List<string>();
            foreach (var kv in mouse.Where(x => x.Value.Count > 1).OrderBy(x => x.Key))
                lines.Add($"{label}: Mouse '{kv.Key}' mapped by: {string.Join(", ", kv.Value)}");
            foreach (var kv in keys.Where(x => x.Value.Count > 1).OrderBy(x => x.Key))
                lines.Add($"{label}: Key '{kv.Key}' mapped by: {string.Join(", ", kv.Value)}");

            if (lines.Count == 0)
                return string.Empty;

            return "Duplicate mapping warning:\n" + string.Join("\n", lines) + "\n\n";
        }
    }
}
