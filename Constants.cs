using System.Threading;

namespace Nebula
{
    /// <summary>
    /// 定数クラス
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// 設定ファイルのパス
        /// </summary>
        public const string SETTINGS_FOOTER = "Settings\\";

        /// <summary>
        /// Form1の設定が記載されたファイル
        /// </summary>
        public const string FORM1_FILE_NAME = $"{SETTINGS_FOOTER}Form1.json";

        /// <summary>
        /// IPアドレスの確認に使用する正規表現
        /// </summary>
        public const string IPRegexPattern = @"^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$";

        /// <summary>
        /// MVアドレスの確認に使用する正規表現
        /// </summary>
        public const string HexRegexPattern = "^[0-9A-Fa-f]{8}$";

        /// <summary>
        /// リソースまたはリソースのプールに同時にアクセスできるスレッドの数を制限
        /// </summary>
        public static readonly SemaphoreSlim Semaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// ディレクトリのパス
        /// </summary>
        public static readonly string _directoryPath = "codes";

        /// <summary>
        /// XMLファイルのパス
        /// </summary>
        public static readonly string _xmlFilePath = "codes/codes.xml";

        /// <summary>
        /// キャンセルトークンソース
        /// </summary>
        public static CancellationTokenSource _saveCancellationTokenSource = new CancellationTokenSource();
    }
}