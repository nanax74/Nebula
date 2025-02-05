using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace Nebula
{
    public static class XML
    {
        private static readonly string _directoryPath = "codes";            // ディレクトリのパス
        private static readonly string _xmlFilePath = "codes/codes.xml";    // XMLファイルのパス
        private static CancellationTokenSource _saveCancellationTokenSource = new CancellationTokenSource();    // キャンセルトークンソース
        private static readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);                              // ファイルアクセスのセマフォ

        private static async Task EnsureXmlFileExistsAsync()
        {
            if (!Directory.Exists(_directoryPath)) Directory.CreateDirectory(_directoryPath);   // ディレクトリが存在しない場合は作成

            if (!File.Exists(_xmlFilePath))
            {
                await InitializeXmlFileAsync();
            }
            else
            {
                try
                {
                    // ファイルが存在する場合、内容を確認
                    using (var stream = new FileStream(_xmlFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        var xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);
                        if (xdoc.Root == null || xdoc.Root.Name != "codes")
                        {
                            // ルート要素が存在しないか、期待されるルート要素でない場合は初期化
                            await InitializeXmlFileAsync();
                        }
                    }
                }
                catch (XmlException)
                {
                    // XMLファイルが破損している場合は初期化
                    await InitializeXmlFileAsync();
                }
            }
        }

        // XMLファイルを初期化するヘルパーメソッド
        private static async Task InitializeXmlFileAsync()
        {
            using (StreamWriter writer = new StreamWriter(_xmlFilePath, false, Encoding.Unicode))
            {
                XDocument xdoc = new XDocument(new XElement("codes"));
                await writer.WriteAsync(xdoc.ToString());
            }
        }

        // Add new code entry to XML
        public static async Task AddCodeAsync(int index, string name, string code, string comment, string tag, bool isEnabled, CancellationToken cancellationToken)
        {
            try
            {
                await EnsureXmlFileExistsAsync();

                // 非同期でXMLを読み込む
                using FileStream fileStream = new FileStream(_xmlFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader stream = new StreamReader(fileStream, Encoding.Unicode);
                using StreamWriter writer = new StreamWriter(_xmlFilePath, false, Encoding.Unicode);

                string xmlContent = await stream.ReadToEndAsync();

                XDocument xdoc = XDocument.Parse(xmlContent);

                xdoc.Element("codes")?.Add(new XElement("entry",
                    new XElement("name", name),
                    new XElement("code", code),
                    new XElement("comment", comment),
                    new XElement("tag", tag),
                    new XElement("enabled", isEnabled)));

                // 非同期でXMLを書き込む
                await writer.WriteAsync(xdoc.ToString());
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }


        // Delete code entry by index
        public static async Task DeleteCodeAsync(int index, CancellationToken cancellationToken)
        {
            try
            {
                await EnsureXmlFileExistsAsync();
                var xdoc = await Task.Run(() => XDocument.Load(_xmlFilePath, LoadOptions.None));

                var entries = xdoc.Descendants("entry").ToList();
                if (index >= 0 && index < entries.Count)
                {
                    entries[index].Remove();
                    await Task.Run(() => xdoc.Save(_xmlFilePath));
                }
                else
                {
                    ShowWarning("指定されたインデックスが無効です。");
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        public static async Task LoadCheckedListBoxFromXMLAsync(Nebula nebula)
        {
            await _fileLock.WaitAsync(); // ファイルアクセスのロックを取得
            try
            {
                await EnsureXmlFileExistsAsync(); // XMLファイルの存在を確認し、必要に応じて作成

                XDocument xdoc = null;
                int retryCount = 3;
                while (retryCount > 0)
                {
                    try
                    {
                        using (var stream = new FileStream(_xmlFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);
                        }
                        break; // 成功した場合はループを抜ける
                    }
                    catch (IOException)
                    {
                        retryCount--;
                        if (retryCount == 0) throw; // リトライ回数が0になったら例外を再スロー
                        await Task.Delay(100); // 少し待ってから再試行
                    }
                    catch (XmlException)
                    {
                        // XMLファイルが空の場合、新しいドキュメントを作成
                        xdoc = new XDocument(new XElement("codes"));
                        break;
                    }
                }

                var items = xdoc.Descendants("entry").Select(entry => new CodeData()
                {
                    Name = entry.Element("name")?.Value,
                    Code = entry.Element("code")?.Value,
                    Comment = entry.Element("comment")?.Value,
                    Tag = entry.Element("tag")?.Value,
                    Enabled = bool.TryParse(entry.Element("enabled")?.Value, out var result) && result
                }).ToList();

                nebula.CheckedListBoxCode.BeginInvoke(new Action(() =>
                {
                    nebula.CheckedListBoxCode.Items.Clear();
                    foreach (var item in items)
                        nebula.CheckedListBoxCode.Items.Add(item, item.Enabled);
                }));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _fileLock.Release(); // ファイルアクセスのロックを解放
            }
        }

        public static async Task SaveXMLAsync(string element, string value, int index)
        {
            _saveCancellationTokenSource?.Cancel(); // 前回の処理をキャンセル

            _saveCancellationTokenSource = new CancellationTokenSource(); // 新しいトークンソースを作成
            CancellationToken token = _saveCancellationTokenSource.Token; // トークンを取得

            await _fileLock.WaitAsync(); // ファイルアクセスのロックを取得
            try
            {
                await EnsureXmlFileExistsAsync(); // XMLファイルの存在を確認し、必要に応じて作成

                XDocument xdoc = null;
                int retryCount = 3;
                while (retryCount > 0)
                {
                    try
                    {
                        using (var stream = new FileStream(_xmlFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, token);
                        }
                        break; // 成功した場合はループを抜ける
                    }
                    catch (IOException)
                    {
                        retryCount--;
                        if (retryCount == 0) throw; // リトライ回数が0になったら例外を再スロー
                        await Task.Delay(100); // 少し待ってから再試行
                    }
                    catch (XmlException)
                    {
                        // XMLファイルが空の場合、新しいドキュメントを作成
                        xdoc = new XDocument(new XElement("codes"));
                        break;
                    }
                }

                var entries = xdoc.Root.Elements("entry").ToList();
                if (index >= 0 && index < entries.Count)
                {
                    entries[index].Element(element)?.SetValue(value);

                    // 一時ファイルのパスを設定
                    string tempFilePath = _xmlFilePath + ".tmp";

                    retryCount = 3;
                    while (retryCount > 0)
                    {
                        try
                        {
                            // 一時ファイルに保存
                            using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (var writer = new StreamWriter(stream))
                            {
                                await xdoc.SaveAsync(writer, SaveOptions.None, token);
                            }

                            // 一時ファイルを本来のファイルに置き換え
                            File.Replace(tempFilePath, _xmlFilePath, null);
                            break; // 成功した場合はループを抜ける
                        }
                        catch (IOException)
                        {
                            retryCount--;
                            if (retryCount == 0)
                            {
                                // 保存に失敗した場合、一時ファイルを削除
                                if (File.Exists(tempFilePath))
                                {
                                    File.Delete(tempFilePath);
                                }
                                throw; // 例外を再スロー
                            }
                            await Task.Delay(100); // 少し待ってから再試行
                        }
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // タスクがキャンセルされた場合の処理
            }
            catch (OperationCanceledException)
            {
                // キャンセルされた場合の処理
            }
            catch (IOException ex)
            {
                ShowError(ex); // IOExceptionの処理
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _fileLock.Release(); // ファイルアクセスのロックを解放
            }
        }

        // Common error display methods
        private static void ShowError(Exception ex) => MessageBox.Show($"エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        private static void ShowWarning(string message) => MessageBox.Show(message, "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}