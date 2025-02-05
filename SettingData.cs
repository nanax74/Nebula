namespace Nebula
{
    /// <summary>
    /// 設定ファイルのデータ構造定義クラス
    /// </summary>
    public class SettingData
    {
        /// <summary>
        /// IPアドレス
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// MVアドレス
        /// </summary>
        public string MVAddress { get; set; }

        /// <summary>
        /// 自動更新するかどうか
        /// </summary>
        public bool AutoUpdate { get; set; }

        /// <summary>
        /// 閲覧モード
        /// </summary>
        public string ViewMode { get; set; }
    }
}
