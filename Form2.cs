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
    public partial class FormPoke : Form
    {
        private DataGridView memoryViewer;
        public FormPoke(DataGridView dataGridView)
        {
            InitializeComponent();
            memoryViewer = dataGridView;
        }

        private async void buttonPoke_Click(object sender, EventArgs e)
        {
            await PokeValue();
        }

        private async void textBoxValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                await PokeValue();
            }
        }

        private async Task PokeValue()
        {
            Nebula nebula = new Nebula();

            uint? valueN = TCPGecko.ParseValue(textBoxValue.Text);
            uint value;
            if (valueN != null)
            {
                value = valueN.Value;
            }
            else
            {
                return;
            }

            foreach (DataGridViewCell cell in memoryViewer.SelectedCells)
            {
                if (cell.Value != null)
                {
                    uint address = Nebula.GetAddressFromMVCell(memoryViewer, cell);
                    await TCPGecko.WriteUInt32(address, value);
                }
            }
        }
    }
}
