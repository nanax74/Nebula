using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nebula
{
    internal class CodeData
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public string MachineCode { get; set; }
        public string Comment { get; set; }
        public string Tag { get; set; }
        public bool Enabled { get; set; }
        public override string ToString() => Name;
    }
}
