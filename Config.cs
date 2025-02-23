using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SZ_Extractor_Server
{
    public class Config
    {
        public int Port { get; set; }
        public bool BindToAllInterfaces { get; set; } = false;
    }
}
