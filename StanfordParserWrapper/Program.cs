using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace StanfordParserWrapper
{
    class Program
    {
        static void Main(string[] args)
        {
            CommandLineOptions cmdOpts = new CommandLineOptions();
            if (args != null && args.Length > 0)
            {
                foreach (string arg in args)
                {
                    if (arg == "-V2")
                    {
                        //use parser_result2
                        cmdOpts.shouldUseVersion2 = true;
                        Console.WriteLine("Sending parser_result2 messages (for Cerebella)");
                    }
                }
            }

            if (!cmdOpts.shouldUseVersion2)
            {
                Console.WriteLine("Sending parser_result messages (for NVBG)");
            }
            VHMsgThread.Initialize(cmdOpts);
            Thread t = new Thread(new ThreadStart(VHMsgThread.MessageLoop));
            t.Start();

            while (t.IsAlive)
            {
                Thread.Sleep(1000);
            }

            t.Join();

            Console.WriteLine("\nClosing app\n");
        }
    }

    public class CommandLineOptions
    {
        public bool shouldUseVersion2 = false;
    }
}
