using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace ConsoleApp1.Classes
{
    class Client
    {
        public string Token { get; set; }
        public DateTime DateConnect { get; set; }

        public Client() { 
            Random random = new Random();
            string Chars = "";
            this.Token = new string(Enumerable.Repeat(Chars, 15).Select(x => x[random.Next(Chars.Length)]).ToArray());
            DateConnect = DateTime.Now;
        }
    }
}
