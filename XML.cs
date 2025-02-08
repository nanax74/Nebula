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
        private static async Task EnsureXmlFileExistsAsync()
        {
            if (!Directory.Exists(Constants.CODES_FOOTER))
            {
                Directory.CreateDirectory(Constants.CODES_FOOTER);   // ディレクトリが存在しない場合は作成
            }

            if (!File.Exists(Constants.CODES_FILE_NAME))
            {
                await InitializeXmlFileAsync();
            }
            else
            {
                try
                {
                    // ファイルが存在する場合、内容を確認
                    using FileStream stream = new FileStream(Constants.CODES_FILE_NAME, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using StreamReader reader = new StreamReader(stream);
                    XDocument xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);
                    if (xdoc.Root == null || xdoc.Root.Name != "codes")
                    {
                        // ルート要素が存在しないか、期待されるルート要素でない場合は初期化
                        await InitializeXmlFileAsync();
                    }
                }
                catch (XmlException)
                {
                    // XMLファイルが破損している場合は初期化
                    await InitializeXmlFileAsync();
                }
            }
        }

        /// <summary>
        /// XMLファイルを初期化するヘルパーメソッド
        /// </summary>
        /// <returns></returns>
        private static async Task InitializeXmlFileAsync()
        {
            using StreamWriter writer = new StreamWriter(Constants.CODES_FILE_NAME, false, Encoding.Unicode);
            XDocument xdoc = new XDocument(new XElement("codes"));
            await writer.WriteAsync(xdoc.ToString());
        }

        /// <summary>
        /// XMLファイルに新しいコードエントリを追加
        /// </summary>
        /// <param name="index"></param>
        /// <param name="name"></param>
        /// <param name="code"></param>
        /// <param name="comment"></param>
        /// <param name="tag"></param>
        /// <param name="isEnabled"></param>
        /// <param name="cancellationToken">操作を取り消す通知</param>
        /// <returns></returns>
        public static async Task AddCodeAsync(int index, string name, string code, string comment, string tag, bool isEnabled, CancellationToken cancellationToken)
        {
            try
            {
                await EnsureXmlFileExistsAsync();

                // 非同期でXMLを読み込む
                using FileStream fileStream = new FileStream(Constants.CODES_FILE_NAME, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(fileStream, Encoding.Unicode);
                using StreamWriter writer = new StreamWriter(Constants.CODES_FILE_NAME, false, Encoding.Unicode);

                string xmlContent = await reader.ReadToEndAsync(cancellationToken);

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
                XDocument xdoc = await Task.Run(() => XDocument.Load(Constants.CODES_FILE_NAME, LoadOptions.None));

                System.Collections.Generic.List<XElement> entries = xdoc.Descendants("entry").ToList();
                if (index >= 0 && index < entries.Count)
                {
                    entries[index].Remove();
                    await Task.Run(() => xdoc.Save(Constants.CODES_FILE_NAME));
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
            await Constants.Semaphore.WaitAsync(); // ファイルアクセスのロックを取得
            try
            {
                await EnsureXmlFileExistsAsync(); // XMLファイルの存在を確認し、必要に応じて作成

                XDocument xdoc = null;
                int retryCount = 3;
                while (retryCount > 0)
                {
                    try
                    {
                        using FileStream stream = new FileStream(Constants.CODES_FILE_NAME, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using StreamReader reader = new StreamReader(stream);
                        xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);
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

                System.Collections.Generic.List<CodeData> items = xdoc.Descendants("entry").Select(entry => new CodeData()
                {
                    Name = entry.Element("name")?.Value,
                    Code = entry.Element("code")?.Value,
                    Comment = entry.Element("comment")?.Value,
                    Tag = entry.Element("tag")?.Value,
                    Enabled = bool.TryParse(entry.Element("enabled")?.Value, out bool result) && result
                }).ToList();

                nebula.CheckedListBoxCode.BeginInvoke(new Action(() =>
                {
                    nebula.CheckedListBoxCode.Items.Clear();
                    foreach (CodeData item in items)
                        nebula.CheckedListBoxCode.Items.Add(item, item.Enabled);
                }));
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                Constants.Semaphore.Release(); // ファイルアクセスのロックを解放
            }
        }

        public static async Task SaveXMLAsync(string element, string value, int index)
        {
            Constants._saveCancellationTokenSource?.Cancel(); // 前回の処理をキャンセル

            CancellationToken token = Constants._saveCancellationTokenSource.Token; // トークンを取得

            await Constants.Semaphore.WaitAsync(); // ファイルアクセスのロックを取得
            try
            {
                await EnsureXmlFileExistsAsync(); // XMLファイルの存在を確認し、必要に応じて作成

                XDocument xdoc = null;
                int retryCount = 3;
                while (retryCount > 0)
                {
                    try
                    {
                        using FileStream stream = new FileStream(Constants.CODES_FILE_NAME, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using StreamReader reader = new StreamReader(stream);
                        xdoc = await XDocument.LoadAsync(reader, LoadOptions.None, token);
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

                System.Collections.Generic.List<XElement> entries = xdoc.Root.Elements("entry").ToList();
                if (index >= 0 && index < entries.Count)
                {
                    entries[index].Element(element)?.SetValue(value);

                    // 一時ファイルのパスを設定
                    string tempFilePath = Constants.CODES_FILE_NAME + ".tmp";

                    retryCount = 3;
                    while (retryCount > 0)
                    {
                        try
                        {
                            // 一時ファイルに保存
                            using (FileStream stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                            using (StreamWriter writer = new StreamWriter(stream))
                            {
                                await xdoc.SaveAsync(writer, SaveOptions.None, token);
                            }

                            // 一時ファイルを本来のファイルに置き換え
                            File.Replace(tempFilePath, Constants.CODES_FILE_NAME, null);
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
                Constants.Semaphore.Release(); // ファイルアクセスのロックを解放
            }
        }

        // Common error display methods
        private static void ShowError(Exception ex) => MessageBox.Show($"エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        private static void ShowWarning(string message) => MessageBox.Show(message, "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}