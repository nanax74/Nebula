using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.ComponentModel;
using static Nebula.Nebula;

namespace Nebula
{
    public static class TCPGecko
    {
        public static async Task<uint> ReadUInt32(uint address)
        {
            if (Nebula.stream != null)
            {
                uint byteNum = 4;
                byte[] command = new byte[9];
                byte[] startAddressCommand = new byte[4];
                byte[] endAddressCommand = new byte[4];

                command[0] = 0x04;
                BitConverter.GetBytes(address).CopyTo(startAddressCommand, 0);
                BitConverter.GetBytes(address + byteNum).CopyTo(endAddressCommand, 0);
                Array.Reverse(startAddressCommand);
                Array.Reverse(endAddressCommand);
                startAddressCommand.CopyTo(command, 1);
                endAddressCommand.CopyTo(command, 5);

                await Nebula.stream.WriteAsync(command, 0, command.Length);

                byte[] buffer = new byte[byteNum + 1];
                await Nebula.stream.ReadAsync(buffer, 0, buffer.Length);

                Array.Reverse(buffer);

                return BitConverter.ToUInt32(buffer, 0);
            }
            return 0;
        }

        public static async Task<byte[]> ReadMemory(uint startAddress, uint endAddress)
        {
            if (startAddress >= endAddress)
            {
                await Task.Run(() => MessageBox.Show("無効なアドレス範囲です。開始アドレスが終了アドレス以上です。"));
                return null;
            }

            if (Nebula.stream == null || !Nebula.stream.CanRead)
            {
                await Task.Run(() => MessageBox.Show("ストリームが初期化されていないか、読み取り不能です。"));
                return null;
            }

            try
            {
                byte[] command = new byte[9];
                BitConverter.GetBytes(startAddress).Reverse().ToArray().CopyTo(command, 1);
                BitConverter.GetBytes(endAddress).Reverse().ToArray().CopyTo(command, 5);
                command[0] = 0x04; // メモリ読み取りコマンド

                await Nebula.stream.WriteAsync(command, 0, command.Length);
                await Nebula.stream.FlushAsync();

                uint byteCount = endAddress - startAddress;
                byte[] buffer = new byte[byteCount + 1];
                int totalBytesRead = 0;

                using (CancellationTokenSource cts = new CancellationTokenSource(5000))
                {
                    while (totalBytesRead < buffer.Length)
                    {
                        int bytesRead = await Nebula.stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, cts.Token);
                        if (bytesRead == 0)
                        {
                            await Task.Run(() => MessageBox.Show("接続が切断されました。"));
                            return null;
                        }

                        if (buffer[0] == 0xB0)
                        {
                            byte[] leanBuffer = buffer.Skip(1).ToArray();
                            Array.Fill<byte>(leanBuffer, 0);
                            return leanBuffer;
                        }

                        if (buffer[0] == 0xFF)
                        {
                            await Task.Run(() => MessageBox.Show("無効なデータが返されました。"));
                            return null;
                        }

                        totalBytesRead += bytesRead;
                    }
                }

                return buffer.Skip(1).ToArray();
            }
            catch (OperationCanceledException)
            {
                await Task.Run(() => MessageBox.Show("メモリ読み取りがタイムアウトしました。"));
                return null;
            }
            catch (IOException ex)
            {
                await Task.Run(() => MessageBox.Show($"I/Oエラーが発生しました: {ex.Message}"));
                return null;
            }
            catch (Exception ex)
            {
                await Task.Run(() => MessageBox.Show($"予期しないエラーが発生しました: {ex.Message}"));
                return null;
            }
        }

        public static async Task WriteUInt32(uint address, uint value)
        {
            if (Nebula.stream != null)
            {
                byte[] command = new byte[9];
                byte[] addressCommand = new byte[4];
                byte[] valueCommand = new byte[4];

                command[0] = 0x03;
                BitConverter.GetBytes(address).CopyTo(addressCommand, 0);
                BitConverter.GetBytes(value).CopyTo(valueCommand, 0);
                Array.Reverse(addressCommand);
                Array.Reverse(valueCommand);
                addressCommand.CopyTo(command, 1);
                valueCommand.CopyTo(command, 5);

                await Nebula.stream.WriteAsync(command, 0, command.Length);
            }
        }

        public static async Task WriteKernel(uint address, uint value)
        {
            if (Nebula.stream != null)
            {
                byte[] command = new byte[9];
                byte[] addressCommand = new byte[4];
                byte[] valueCommand = new byte[4];

                command[0] = 0x0B;
                BitConverter.GetBytes(address).CopyTo(addressCommand, 0);
                BitConverter.GetBytes(value).CopyTo(valueCommand, 0);
                Array.Reverse(addressCommand);
                Array.Reverse(valueCommand);
                addressCommand.CopyTo(command, 1);
                valueCommand.CopyTo(command, 5);

                await Nebula.stream.WriteAsync(command, 0, command.Length);
            }
        }

        public static async Task<uint> Pointer(uint address, uint offset)
        {
            return await ReadUInt32(address) + offset;
        }

        public static async Task ExecuteAssembly(string assemblyCode)
        {
            if (Nebula.stream != null)
            {
                try
                {
                    // デバッグ情報の表示
                    Debug.WriteLine($"Original assembly code: {assemblyCode}");

                    // 前処理: 空白と改行を削除
                    assemblyCode = assemblyCode.Replace(" ", "").Replace("\n", "").Replace("\r", "");

                    // デバッグ情報の表示
                    Debug.WriteLine($"Processed assembly code: {assemblyCode}");

                    // 16進数文字列の長さが偶数であることを確認
                    if (assemblyCode.Length % 2 != 0)
                    {
                        throw new ArgumentException("Hex string must have an even length.");
                    }

                    // 16進数文字列が有効な文字で構成されていることを確認
                    if (!Regex.IsMatch(assemblyCode, @"\A\b[0-9a-fA-F]+\b\Z"))
                    {
                        throw new ArgumentException("Assembly code contains invalid characters.");
                    }

                    byte[] command = new byte[assemblyCode.Length / 2 + 5];
                    byte[] assemblyCodeCommand = new byte[assemblyCode.Length / 2];
                    int assemblyBytes = assemblyCode.Length / 2;
                    byte[] assemblyBytesBuffer = new byte[4];
                    command[0] = 0x81;

                    for (int i = 0; i < assemblyCode.Length; i += 2)
                    {
                        assemblyCodeCommand[i / 2] = Convert.ToByte(assemblyCode.Substring(i, 2), 16);
                    }

                    BitConverter.GetBytes(assemblyBytes).CopyTo(assemblyBytesBuffer, 0);
                    Array.Reverse(assemblyBytesBuffer);
                    assemblyBytesBuffer.CopyTo(command, 1);
                    assemblyCodeCommand.CopyTo(command, 5);

                    Debug.WriteLine($"Final command: {BitConverter.ToString(command)}");

                    await Nebula.stream.WriteAsync(command, 0, command.Length);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show($"Assembly code format error: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"ExecuteAssembly中にエラーが発生しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public static uint[] ByteArrayToUIntArray(byte[] byteArray)
        {
            if (byteArray == null)
            {
                MessageBox.Show(nameof(byteArray), "バイト配列がnullです。");
                return null;
            }

            if (byteArray.Length % 4 != 0)
            {
                MessageBox.Show("バイト配列の長さが4の倍数ではありません。");
                return null;
            }

            uint[] uintArray = new uint[byteArray.Length / 4];
            Buffer.BlockCopy(byteArray, 0, uintArray, 0, byteArray.Length);

            return ConvertEndianUIntArray(uintArray);
        }

        public static uint[] ConvertEndianUIntArray(uint[] input)
        {
            // 出力用配列
            uint[] result = new uint[input.Length];

            for (int i = 0; i < input.Length; i++)
            {
                // uint をバイト配列に変換
                byte[] bytes = BitConverter.GetBytes(input[i]);

                // バイト配列を逆順にしてエンディアン変換
                Array.Reverse(bytes);

                // バイト配列を uint に戻して結果配列に格納
                result[i] = BitConverter.ToUInt32(bytes, 0);
            }

            return result;
        }


        public static async Task<uint> EvaluateExpression(string input)
        {
            input = input.Replace(" ", ""); // 空白を削除

            string formulaPattern = @"-?(0x[0-9A-Fa-f]+|\d+)\s*[\+\-]\s*-?(0x[0-9A-Fa-f]+|\d+)";
            string pointerPattern = @"\[(0x[0-9A-Fa-f]{1,8}|[0-9]{1,10})\]";

            string currentInput = input;
            int maxIterations = 100;
            int iterationCount = 0;

            while ((Regex.IsMatch(currentInput, pointerPattern) || Regex.IsMatch(currentInput, formulaPattern)) && iterationCount < maxIterations)
            {
                iterationCount++;
                string originalInput = currentInput;

                // 演算子の処理
                currentInput = Regex.Replace(currentInput, formulaPattern, match =>
                {
                    string expression = match.Value;
                    uint result = Evaluate(expression);
                    return result.ToString();
                });

                // ポインタの処理（非同期）
                MatchCollection matchesP = Regex.Matches(currentInput, pointerPattern);
                if (matchesP.Count > 0)
                {
                    foreach (Match match in matchesP)
                    {
                        string expression = match.Value;
                        string pointerExpression = expression.Trim('[', ']');
                        uint address = ParseValue(pointerExpression) ?? 0;
                        uint value = await ReadUInt32(address);
                        currentInput = currentInput.Replace(expression, value.ToString());
                    }
                }

                // 入力が変わらなければループを終了
                if (currentInput == originalInput)
                {
                    throw new InvalidOperationException("無限ループの可能性があります: 入力が変化していません。");
                }
            }

            if (iterationCount >= maxIterations)
            {
                throw new InvalidOperationException("最大ループ回数を超えました。入力に問題がある可能性があります。");
            }

            return ParseValue(currentInput) ?? 0;
        }

        // 式を計算する関数
        public static uint Evaluate(string expression)
        {
            // 空白を削除
            expression = expression.Replace(" ", "");

            // 演算子を特定
            if (expression.Contains("+"))
            {
                string[] parts = expression.Split('+'); // "+" で分割
                uint left = ParseValue(parts[0]) ?? 0;    // 左の数値
                uint right = ParseValue(parts[1]) ?? 0;   // 右の数値

                return left + right;               // 足し算
            }
            else if (expression.Contains("-"))
            {
                string[] parts = expression.Split('-'); // "-" で分割
                uint left = ParseValue(parts[0]) ?? 0;    // 左の数値
                uint right = ParseValue(parts[1]) ?? 0;   // 右の数値

                return left - right;               // 引き算
            }
            return 0;
        }

        // アドレスを解析する関数（16進数または10進数を扱う）
        public static uint? ParseValue(string value)
        {
            uint result;

            // 16進数の場合（"0x"で始まる）
            if (value.StartsWith("0x"))
            {
                string hexValue = value[2..]; // "0x"を取り除く
                if (UInt32.TryParse(hexValue, System.Globalization.NumberStyles.HexNumber, null, out result))
                {
                    return result;
                }
                else
                {
                    MessageBox.Show("無効な16進数形式です。");
                    return null;
                }
            }
            // 10進数の場合
            else
            {
                if (UInt32.TryParse(value, System.Globalization.NumberStyles.Integer, null, out result))
                {
                    return result;
                }
                else
                {
                    MessageBox.Show("無効な10進数形式です。");
                    return null;
                }
            }
        }

        private static async Task<string> RunProcess(string fileName, string arguments)
        {
            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // プロセスを開始
            process.Start();

            // 標準出力と標準エラー出力の内容を非同期でキャプチャ
            string output = await process.StandardOutput.ReadToEndAsync();
            string errors = await process.StandardError.ReadToEndAsync();

            // プロセス終了を非同期で待機
            await process.WaitForExitAsync();

            // エラーメッセージの基準ディレクトリ
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // エラーメッセージを整形
            if (!string.IsNullOrEmpty(errors))
            {
                // エラー内の絶対パスを相対パスに変換
                errors = errors.Replace(appDirectory, "");
            }

            // エラーメッセージがあれば、それを返す
            if (!string.IsNullOrEmpty(errors))
            {
                return $"Error: {errors}";
            }

            // エラーがなければ、標準出力を返す
            return output;
        }

        // リソースファイルを一時ファイルに展開する
        private static async Task ResourceInit(string resourceName, string outputPath)
        {
            // リソースストリームを取得
            using Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (resourceStream == null)
                throw new Exception($"リソース '{resourceName}' が見つかりません。");

            // リソースをファイルに非同期で書き込む
            using FileStream fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192, useAsync: true);
            await resourceStream.CopyToAsync(fileStream);
        }

        public static async Task<string> AssemblePPC(string assemblyCode)
        {
            // 作業ディレクトリとファイルパスを設定
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string ppcDirectory = Path.Combine(appDirectory, "PPC");

            // PPCフォルダが存在しない場合は作成
            if (!Directory.Exists(ppcDirectory))
            {
                Directory.CreateDirectory(ppcDirectory);
            }

            // powerpc-eabiツールチェーンの埋め込みリソース名
            string assemblerResource = "Nebula.powerpc-eabi-as.exe"; // powerpc-eabi-asの埋め込みリソース名
            string nameListResource = "Nebula.powerpc-eabi-nm.exe";       // nmの埋め込みリソース名
            string objcopyResource = "Nebula.powerpc-eabi-objcopy.exe"; // powerpc-eabi-objcopyの埋め込みリソース名

            // powerpc-eabiツールチェーンをアプリケーションディレクトリに展開
            string assemblerPath = Path.Combine(ppcDirectory, "powerpc-eabi-as.exe");
            string nameListPath = Path.Combine(ppcDirectory, "powerpc-eabi-nm.exe");
            string objcopyPath = Path.Combine(ppcDirectory, "powerpc-eabi-objcopy.exe");

            await ResourceInit(assemblerResource, assemblerPath);
            await ResourceInit(nameListResource, nameListPath);
            await ResourceInit(objcopyResource, objcopyPath);

            // 各ファイルパスを設定
            string tempInputFile = Path.Combine(ppcDirectory, "code.s");
            string tempOutputFileO = Path.Combine(ppcDirectory, "code.o");
            string tempOutputFileBin = Path.Combine(ppcDirectory, "code.bin");

            // アセンブリコードをファイルに保存（最後に改行を追加）
            await File.WriteAllTextAsync(tempInputFile, assemblyCode + Environment.NewLine);

            string errors;

            // アセンブリファイルをオブジェクトファイルに変換
            errors = await RunProcess(assemblerPath, $"-mregnames -mgekko \"{tempInputFile}\" -o \"{tempOutputFileO}\"");

            if (!string.IsNullOrEmpty(errors))
            {
                return $"アセンブリエラー: {errors}";
            }

            // nmを使って未定義のシンボルをチェック
            errors = await RunProcess(nameListPath, $"\"{tempOutputFileO}\"");

            // "U "で始まる未定義のシンボルを抽出
            List<string> undefinedSymbols = errors
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains(" U "))
                .Select(line => line.Split(' ').Last())
                .ToList();

            if (undefinedSymbols.Any())
            {
                return $"未定義シンボルエラー: {string.Join(", ", undefinedSymbols)}";
            }

            // ELFファイルをバイナリに変換
            errors = await RunProcess(objcopyPath, $"-O binary \"{tempOutputFileO}\" \"{tempOutputFileBin}\"");

            if (!string.IsNullOrEmpty(errors))
            {
                return $"オブジェクト変換エラー: {errors}";
            }

            // バイナリを読み込んでフォーマット
            byte[] binData = await File.ReadAllBytesAsync(tempOutputFileBin);

            // 8バイト単位で改行し、4バイトごとにスペースを挿入
            StringBuilder formattedOutput = new();
            for (int i = 0; i < binData.Length; i += 8)
            {
                // 8バイト分のデータを取得
                byte[] chunk = binData.Skip(i).Take(8).ToArray();

                // 前半4バイトと後半4バイトをそれぞれ取得
                string firstPart = string.Concat(chunk.Take(4).Select(b => $"{b:X2}"));
                string secondPart = string.Concat(chunk.Skip(4).Take(4).Select(b => $"{b:X2}"));

                // フォーマットして出力
                formattedOutput.AppendLine($"{firstPart} {secondPart}");
            }

            return formattedOutput.ToString().TrimEnd();
        }
    }
}
