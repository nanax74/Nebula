using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Nebula
{
    public partial class FormInputText : Form
    {
        public string ReturnValue { get; private set; } // 返り値として使用するプロパティ
        public FormInputText()
        {
            InitializeComponent();
        }

        private void Return()
        {
            // TextBoxの内容を返り値に設定
            ReturnValue = textBox.Text;
            DialogResult = DialogResult.OK; // フォームを閉じる
            Close();
        }

        private void button_Click(object sender, EventArgs e)
        {
            Return();
        }

        private void textBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Return();
            }
        }
    }
}
