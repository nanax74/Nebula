using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace Nebula
{
    public partial class Nebula : Form
    {
        private System.Timers.Timer _timer;
        private uint StartMVAddress;
        private string StartMVAddressExpression;
        private CancellationTokenSource _debounceTokenSource;

        //他クラスからアクセスするためのプロパティ
        public CheckedListBox CheckedListBoxCode => checkedListBoxCode;

        public Nebula()
        {
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            _timer = new System.Timers.Timer(1000 / 60)
            {
                AutoReset = true
            };
            _timer.Elapsed += async (_, _) => await UpdateAsync();
            _timer.Start();
        }

        private async void Nebula_Load(object sender, EventArgs e)
        {
            await InitializeNebula();
        }

        private async Task InitializeNebula()
        {
            await LoadSettingsAsync();
            StartMVAddressExpression = textBoxMVAddress.Text;
            await XML.LoadCheckedListBoxFromXMLAsync(this);
            SetRichTextBoxEnabled();
            UpdateControls();
        }

        private async Task LoadSettingsAsync()
        {
            // データクラスを使用しないパターン
            // 設定ファイルを開き、内容をJSON文字列としてパースしJObject型を得る
            //JObject jObj = JObject.Parse(File.ReadAllText(json_path));

            // JSONのパス指定で目的の値を抽出(見つからない場合は例外をスロー)
            //JToken MVAddr = jObj.SelectToken("Address", true);
            //bool Update = (bool)jObj.SelectToken("AutoUpdate", true);
            //JToken IPAddr = jObj.SelectToken("IPAddress", true);
            //JToken Mode = jObj.SelectToken("ViewMode", true);

            // それぞれ反映
            //textBoxMVAddress.Text = MVAddr.ToString();
            //checkBoxMVAutoUpdate.Checked = Update;
            //textBoxIP.Text = IPAddr.ToString();
            //comboBoxMVViewMode.Text = Mode.ToString();

            // Nebulaのコードを格納しているアセンブリを取得
            Assembly asm = Assembly.GetExecutingAssembly();

            // Nebulaのバージョン情報を取得
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(asm.Location);
            // 「Nebula+」の後にコミット番号？が入っているので削除
            int index = fvi.ProductVersion.IndexOf('+');
            string version = fvi.ProductVersion.Substring(0, index);

            // フォームのタイトルに反映
            Text = $"{fvi.FileDescription} {version}";

            // 設定ファイルのディレクトリの存在確認
            if (!Directory.Exists(Constants.SETTINGS_FOOTER))
            {
                // 設定ファイルのディレクトリ作成
                Directory.CreateDirectory(Constants.SETTINGS_FOOTER);
            }

            // 設定ファイルの存在確認
            if (!File.Exists(Constants.FORM1_FILE_NAME))
            {
                // 設定ファイルを作成
                File.Create(Constants.FORM1_FILE_NAME).Close();

                // デフォルトの設定値を作成
                SettingData data = new SettingData
                {
                    IPAddress = "192.168.",
                    MVAddress = "0x10000000",
                    AutoUpdate = true,
                    ViewMode = "Hexadecimal"
                };

                // シリアライズして設定ファイルへ書き込む文字列を作成
                string jsonStr = JsonConvert.SerializeObject(data, Formatting.Indented);
                // 設定ファイルへ書き込み
                File.WriteAllText(Constants.FORM1_FILE_NAME, jsonStr);
            }

            // 設定ファイルの内容を文字列として読み込み
            string jsonStr1 = await File.ReadAllTextAsync(Constants.FORM1_FILE_NAME);
            // 読み込んだ文字列をSettingクラスを使ってデシリアライズ(JSON形式のデータからC#が扱えるものに変換すること)
            SettingData data1 = JsonConvert.DeserializeObject<SettingData>(jsonStr1);

            // それぞれ反映
            textBoxIP.Text = data1.IPAddress;
            textBoxMVAddress.Text = data1.MVAddress;
            checkBoxMVAutoUpdate.Checked = data1.AutoUpdate;
            comboBoxMVViewMode.Text = data1.ViewMode;

            // DataGirdViewのチラつきを防止
            // DataGridViewのTypeを取得
            Type dgvtype = typeof(DataGridView);
            // プロパティ設定の取得
            PropertyInfo dgvPropertyInfo = dgvtype.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            // 対象のDataGridViewにtrueをセットする
            dgvPropertyInfo.SetValue(dataGridViewMV, true, null);

            ValidateInputs();
        }

        private void ValidateInputs()
        {
            textBoxIP.BackColor = Regex.IsMatch(textBoxIP.Text, Constants.IPRegexPattern) ? Color.Lime : Color.Red;
            textBoxMVAddress.BackColor = Regex.IsMatch(textBoxMVAddress.Text, Constants.HexRegexPattern) ? Color.Lime : Color.Red;
        }

        private async Task SaveSettingsAsync()
        {
            // Settingクラスを使って、もろもろのプロパティを取得(テキストやチェック状態)
            SettingData setting = new SettingData
            {
                IPAddress = textBoxIP.Text,
                MVAddress = textBoxMVAddress.Text,
                AutoUpdate = checkBoxMVAutoUpdate.Checked,
                ViewMode = comboBoxMVViewMode.Text
            };

            _timer.Stop(); // タイマーを停止
            _timer.Dispose(); // タイマーのリソースを解放

            // シリアライズして設定ファイルへ書き込む文字列を作成
            string jsonStr = JsonConvert.SerializeObject(setting, Formatting.Indented);
            // 設定ファイルへ書き込み
            await File.WriteAllTextAsync(Constants.FORM1_FILE_NAME, jsonStr);
        }

        private async void Nebula_FormClosing(object sender, FormClosingEventArgs e)
        {
            await SaveSettingsAsync();
            _timer?.Stop();
            _timer?.Dispose();
        }

        private async Task UpdateAsync()
        {
            if (!Constants.Semaphore.Wait(0)) return;

            try
            {
                if (checkBoxMVAutoUpdate.Checked)
                {
                    await MVLoadAsync();
                }

                await ExecuteCodes();
            }
            finally
            {
                Constants.Semaphore.Release();
            }
        }

        private async Task ExecuteCodes()
        {
            if (stream != null)
            {
                // CheckedItemsCollectionをList<CodeData>に変換
                List<CodeData> listItems = checkedListBoxCode.CheckedItems.Cast<CodeData>().ToList();

                // 各CodeDataに対して非同期処理を実行
                foreach (CodeData listItem in listItems)
                {
                    if (listItem.MachineCode == null)
                    {
                        // 機械語に変換
                        if (string.IsNullOrWhiteSpace(listItem.Code))
                        {
                            continue;
                        }
                        string machineCode = await TCPGecko.AssemblePPC(listItem.Code);
                        listItem.MachineCode = machineCode;
                    }

                    await TCPGecko.ExecuteAssembly(listItem.MachineCode);
                }
            }
        }

        private async Task MVLoadAsync()
        {
            if (stream == null) return;

            try
            {
                StartMVAddress = await TCPGecko.EvaluateExpression(StartMVAddressExpression);
                uint rowCount = Convert.ToUInt32(dataGridViewMV.RowCount);

                // メモリを非同期で読み込み
                uint[] memory = await Task.Run(async () => TCPGecko.ByteArrayToUIntArray(
                await TCPGecko.ReadMemory(StartMVAddress, StartMVAddress + rowCount * 16)));

                if (memory == null)
                {
                    Invoke(new Action(() =>
                    {
                        textBoxMVAddress.BackColor = Color.Red;
                        MessageBox.Show("メモリの読み取りに失敗しました", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                    return;
                }

                // UIの更新をUIスレッドで実行
                Invoke(new Action(() =>
                {
                    textBoxMVAddress.BackColor = Color.Lime;

                    for (int i = 0; i < memory.Length / 4; i++)
                    {
                        dataGridViewMV.Rows[i].Cells[0].Value = (StartMVAddress + i * 16).ToString("X8");

                        string viewMode = comboBoxMVViewMode.SelectedItem?.ToString();
                        if (viewMode == "Hexadecimal")
                        {
                            for (int j = 0; j < 4; j++)
                                dataGridViewMV.Rows[i].Cells[j + 1].Value = memory[i * 4 + j].ToString("X8");
                        }
                        else if (viewMode == "Floating Point")
                        {
                            for (int j = 0; j < 4; j++)
                                dataGridViewMV.Rows[i].Cells[j + 1].Value =
                                    BitConverter.ToSingle(BitConverter.GetBytes(memory[i * 4 + j]), 0).ToString();
                        }
                        else if (viewMode == "UTF-8")
                        {
                            for (int j = 0; j < 4; j++)
                                dataGridViewMV.Rows[i].Cells[j + 1].Value =
                                    Encoding.UTF8.GetString(BitConverter.GetBytes(memory[i * 4 + j])[..4].Reverse().ToArray());
                        }
                    }
                }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ButtonMVUpdate_Click(object sender, EventArgs e)
        {
            if (!Constants.Semaphore.Wait(0))
            {
                MessageBox.Show("現在他の操作が進行中です。もう一度お試しください。", "通知", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                await MVLoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"例外が発生しました: {ex.Message}\n{ex.StackTrace}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Constants.Semaphore.Release();
            }
        }

        private void CheckBoxMVAutoUpdate_CheckedChanged(object sender, EventArgs e)
        {
            buttonMVUpdate.Enabled = !checkBoxMVAutoUpdate.Checked;
        }

        private void SetRichTextBoxEnabled()
        {
            bool enabled = checkedListBoxCode.SelectedItem != null;
            richTextBoxCode.Enabled = enabled;
            richTextBoxComment.Enabled = enabled;
        }

        private void UpdateCheckedListBoxItem()
        {
            if (checkedListBoxCode.SelectedItem != null)
            {
                ListBox selectedItem = checkedListBoxCode.SelectedItem as ListBox;
                if (selectedItem != null)
                {

                }
            }
        }

        private void UpdateControls()
        {
            if (checkedListBoxCode.SelectedItem != null)
            {
                CodeData selectedItem = checkedListBoxCode.SelectedItem as CodeData;
                if (selectedItem != null)
                {
                    _isTextChangedManually_Code = false;
                    richTextBoxCode.Text = selectedItem.Code ?? "コードなし";
                    _isTextChangedManually_Comment = false;
                    richTextBoxComment.Text = selectedItem.Comment ?? "コメントなし";

                    listBoxTag.Items.Clear();
                    string[] items = selectedItem.Tag.Split(new[] { '\n' }, StringSplitOptions.None); // 改行で分割

                    foreach (string item in items)
                    {
                        listBoxTag.Items.Add(item);
                    }
                }
            }
        }

        private void TextBoxIP_TextChanged(object sender, EventArgs e) => ValidateInputs();

        private void TextBoxMVAddress_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                StartMVAddressExpression = textBoxMVAddress.Text;
            }
        }

        private async void ButtonConnect_Click(object sender, EventArgs e)
        {
            if (buttonConnect.Text == "Connecting...") return;

            try
            {
                buttonConnect.Text = "Connecting...";
                buttonConnect.Enabled = false;

                if (stream == null)
                {
                    client = new TcpClient();

                    var connectTask = client.ConnectAsync(textBoxIP.Text, 7331);
                    var timeoutTask = Task.Delay(5000);

                    if (await Task.WhenAny(connectTask, timeoutTask) == connectTask)
                    {
                        stream = client.GetStream();
                        buttonConnect.Text = "Disconnect";
                    }
                    else
                    {
                        throw new TimeoutException("Connection timed out.");
                    }
                }
                else
                {
                    stream.Close();
                    client.Close();
                    stream = null;
                    client = null;
                    buttonConnect.Text = "Connect";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"接続エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                buttonConnect.Text = "Connect";
            }
            finally
            {
                buttonConnect.Enabled = true;
            }
        }

        public static uint GetAddressFromMVCell(DataGridView dataGridView, DataGridViewCell cell)
        {
            int row = cell.RowIndex;
            int column = cell.ColumnIndex;
            UInt32.TryParse(dataGridView.Rows[row].Cells[0].Value.ToString(), System.Globalization.NumberStyles.HexNumber, null, out uint baseAddress);
            uint address = baseAddress + (uint)(column - 1) * 4;
            return address;
        }

        private void DataGridViewMV_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                toolStripMenuItemPoke.Enabled = stream != null;
                contextMenuStripMV.Show(dataGridViewMV, e.Location);
            }
        }

        private void DataGridViewMV_SelectionChanged(object sender, EventArgs e)
        {
            foreach (DataGridViewCell cell in dataGridViewMV.SelectedCells)
            {
                if (cell.ColumnIndex == 0)
                {
                    cell.Selected = false;
                }
            }
        }

        private void ToolStripMenuItemPoke_Click(object sender, EventArgs e)
        {
            FormPoke formPoke = new FormPoke(dataGridViewMV);
            Point cursorPosition = Cursor.Position;
            int formWidth = formPoke.Width;
            int formHeight = formPoke.Height;
            int x = cursorPosition.X - formWidth / 2;
            int y = cursorPosition.Y - formHeight / 2;
            formPoke.StartPosition = FormStartPosition.Manual;
            formPoke.Location = new Point(x, y);
            formPoke.Show();
        }

        private void CheckedListBoxCode_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (checkedListBoxCode.SelectedItem != null)
                {
                    renameToolStripMenuItem.Enabled = true;
                }
                else
                {
                    renameToolStripMenuItem.Enabled = false;
                }
                contextMenuStripCode.Show(checkedListBoxCode, e.Location);
            }
        }

        private static void ApplySyntaxHighlighting(RichTextBox richTextBox)
        {
            var keywords = new (string Pattern, Color Color)[]
            {
                (@"\b(b|bl|bctr|bctrl)\b", Color.Blue),
                (@"#.*$", Color.Green),
                (@"^\s*\w+:", Color.Purple),
                (@"\b0x[0-9A-Fa-f]+\b|\b\d+\b", Color.DarkOrange)
            };

            int originalSelectionStart = richTextBox.SelectionStart;
            int originalSelectionLength = richTextBox.SelectionLength;

            richTextBox.Select(0, richTextBox.Text.Length);
            richTextBox.SelectionColor = richTextBox.ForeColor;

            foreach (var (pattern, color) in keywords)
            {
                var matches = Regex.Matches(richTextBox.Text, pattern, RegexOptions.Multiline);
                foreach (Match match in matches)
                {
                    richTextBox.Select(match.Index, match.Length);
                    richTextBox.SelectionColor = color;
                }
            }

            richTextBox.Select(originalSelectionStart, originalSelectionLength);
            richTextBox.SelectionColor = richTextBox.ForeColor;
        }

        private async void RichTextBoxAssembly_TextChanged(object sender, EventArgs e)
        {
            ApplySyntaxHighlighting(richTextBoxAssembly);

            if (_debounceTokenSource != null)
            {
                _debounceTokenSource.Cancel();
            }

            _debounceTokenSource = new CancellationTokenSource();
            var token = _debounceTokenSource.Token;

            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;

                if (string.IsNullOrWhiteSpace(richTextBoxAssembly.Text))
                {
                    richTextBoxMachineCode.Text = string.Empty;
                    return;
                }

                richTextBoxMachineCode.Text = "Assembling...";
                string machineCode = await TCPGecko.AssemblePPC(richTextBoxAssembly.Text);
                richTextBoxMachineCode.Text = machineCode;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                richTextBoxMachineCode.Text = $"Error: {ex.Message}";
            }
        }

        private async void ButtonExecuteAssembly_Click(object sender, EventArgs e)
        {
            await TCPGecko.ExecuteAssembly(richTextBoxMachineCode.Text);
        }

        private bool _isTextChangedManually_Code = true;
        private CancellationTokenSource _saveCancellationTokenSource_Code;
        private bool _isTextChangedManually_Comment = true;
        private CancellationTokenSource _saveCancellationTokenSource_Comment;
        private CancellationTokenSource _saveCancellationTokenSource_AddCode;

        private async void RichTextBoxCode_TextChanged(object sender, EventArgs e)
        {
            ApplySyntaxHighlighting(richTextBoxCode);

            if (!_isTextChangedManually_Code)
            {
                _isTextChangedManually_Code = true;
                return;
            }

            _saveCancellationTokenSource_Code?.Cancel();
            _saveCancellationTokenSource_Code = new CancellationTokenSource();
            var token = _saveCancellationTokenSource_Code.Token;

            try
            {
                CodeData selectedItem = checkedListBoxCode.SelectedItem as CodeData;
                if (selectedItem != null)
                {
                    await XML.SaveXMLAsync("code", richTextBoxCode.Text, checkedListBoxCode.SelectedIndex);
                    selectedItem.Code = richTextBoxCode.Text;
                    selectedItem.MachineCode = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void RichTextBoxComment_TextChanged(object sender, EventArgs e)
        {
            ApplySyntaxHighlighting(richTextBoxComment);

            if (!_isTextChangedManually_Comment)
            {
                _isTextChangedManually_Comment = true;
                return;
            }

            _saveCancellationTokenSource_Comment?.Cancel();
            _saveCancellationTokenSource_Comment = new CancellationTokenSource();
            var token = _saveCancellationTokenSource_Comment.Token;

            try
            {
                CodeData selectedItem = checkedListBoxCode.SelectedItem as CodeData;
                if (selectedItem != null)
                {
                    await XML.SaveXMLAsync("comment", richTextBoxComment.Text, checkedListBoxCode.SelectedIndex);
                    selectedItem.Comment = richTextBoxComment.Text;

                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CheckedListBoxCode_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetRichTextBoxEnabled();
            UpdateControls();
        }

        private async void ButtonAddCode_Click(object sender, EventArgs e)
        {
            _saveCancellationTokenSource_AddCode?.Cancel();
            _saveCancellationTokenSource_AddCode = new CancellationTokenSource();
            var token = _saveCancellationTokenSource_AddCode.Token;

            try
            {
                await Task.Delay(500, token);
                if (token.IsCancellationRequested) return;

                string name = "New Code";
                string code = "";
                string comment = "";
                string tag = "";
                bool enabled = true;

                await XML.AddCodeAsync(checkedListBoxCode.SelectedIndex, name, code, comment, tag, enabled, token);

                var listItem = new CodeData { Name = name, Code = code, Comment = comment, Tag = tag, Enabled = enabled };
                checkedListBoxCode.Items.Add(listItem, listItem.Enabled);

                SetRichTextBoxEnabled();
                UpdateControls();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"保存中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void ButtonDeleteCode_Click(object sender, EventArgs e)
        {
            if (checkedListBoxCode.SelectedItem == null)
            {
                MessageBox.Show("コードが選択されていません", "通知", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show("本当に消去しますか？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                await XML.DeleteCodeAsync(checkedListBoxCode.SelectedIndex, CancellationToken.None);
                checkedListBoxCode.Items.Remove(checkedListBoxCode.SelectedItem);
            }
            SetRichTextBoxEnabled();
            UpdateControls();
        }

        private async void ButtonReloadCodeList_Click(object sender, EventArgs e)
        {
            await XML.LoadCheckedListBoxFromXMLAsync(this);
        }

        private async void RenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (FormInputText formInputText = new FormInputText())
            {
                formInputText.StartPosition = FormStartPosition.CenterParent;
                if (formInputText.ShowDialog() == DialogResult.OK)
                {
                    string returnValue = formInputText.ReturnValue;
                    await XML.SaveXMLAsync("name", returnValue, checkedListBoxCode.SelectedIndex);
                    await XML.LoadCheckedListBoxFromXMLAsync(this);
                    UpdateControls();
                }
            }
        }

        private async void ButtonAddTag_Click(object sender, EventArgs e)
        {
            CodeData selectedItem = checkedListBoxCode.SelectedItem as CodeData;
            if (selectedItem != null)
            {
                using (FormInputText formInputText = new FormInputText())
                {
                    formInputText.StartPosition = FormStartPosition.CenterParent;
                    if (formInputText.ShowDialog() == DialogResult.OK)
                    {
                        string returnValue = formInputText.ReturnValue;
                        listBoxTag.Items.Add(returnValue);
                        string tagString = string.Join("\n", listBoxTag.Items.Cast<string>());
                        selectedItem.Tag = tagString;
                        await XML.SaveXMLAsync("tag", tagString, checkedListBoxCode.SelectedIndex);
                    }
                }
            }
        }

        private async void ButtonRemoveTag_Click(object sender, EventArgs e)
        {
            CodeData selectedItem = checkedListBoxCode.SelectedItem as CodeData;
            if (selectedItem != null)
            {
                listBoxTag.Items.Remove(listBoxTag.SelectedItem);
                string tagString = string.Join("\n", listBoxTag.Items.Cast<string>());
                selectedItem.Tag = tagString;
                await XML.SaveXMLAsync("tag", tagString, checkedListBoxCode.SelectedIndex);
            }
        }

        public static NetworkStream stream;
        private TcpClient client;
    }
}