using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using VHMsg;
using System.Collections;


namespace StanfordParserWrapper
{
    class VHMsgThread
    {
        public static VHMsg.Client vhmsg;
        public static bool shouldDie = false;

        static string stanfordParserBatchScript = @"runStanfordParser.bat";
        public static CommandLineOptions cmdOptions;


        public static void Initialize(CommandLineOptions cmdOpts)
        {
            cmdOptions = cmdOpts;
            shouldDie = false;

            vhmsg = new Client();
            vhmsg.OpenConnection();

            vhmsg.MessageEvent += new Client.MessageEventHandler(vhmsg_MessageEvent);
            vhmsg.SendMessage("vrComponent nvb parser");

            vhmsg.SubscribeMessage("vrAllCall");
            vhmsg.SubscribeMessage("vrKillComponent");
            vhmsg.SubscribeMessage("nvbGen_to_parser");
            vhmsg.SubscribeMessage("elvinSim_to_parser");
            vhmsg.SubscribeMessage("nvbGen_ready");
            vhmsg.SubscribeMessage("parser_ready");


        }

        private static string StartStanfordParser(string filePath)
        {
            string currentDir = Directory.GetCurrentDirectory();

            //Directory.SetCurrentDirectory(pathForStandordParser);
            string outputFromParser = string.Empty;
            Process stanfordParser = new Process();
            stanfordParser.StartInfo.FileName = stanfordParserBatchScript;
            stanfordParser.StartInfo.Arguments = "\"" + filePath + "\"";
            //stanfordParser.StartInfo.Arguments = filePath;
            stanfordParser.StartInfo.UseShellExecute = false;
            stanfordParser.StartInfo.RedirectStandardOutput = true;


            stanfordParser.Start();

            stanfordParser.OutputDataReceived += new DataReceivedEventHandler
            (
                delegate(object sender, DataReceivedEventArgs e)
                {
                    using (StreamReader output = stanfordParser.StandardOutput)
                    {
                        outputFromParser = output.ReadToEnd();
                    }
                }
            );

            stanfordParser.WaitForExit();
            outputFromParser = stanfordParser.StandardOutput.ReadToEnd();

            //Directory.SetCurrentDirectory(currentDir);
            return outputFromParser;

        }

        static void vhmsg_MessageEvent(object sender, Message args)
        {
            string[] splitargs = args.s.Split(" ".ToCharArray());
            Console.WriteLine("Message received: " + args.s);
            if (splitargs.Length > 0)
            {
                if (splitargs[0] == "vrAllCall")
                {
                    vhmsg.SendMessage("vrComponent nvb parser");
                }
                else if (splitargs[0] == "vrKillComponent")
                {
                    if (splitargs.Length > 1)
                    {
                        if (splitargs[1].Equals("nvb") || 
                            splitargs[1].Equals("nvb-parser") ||
                            splitargs[1].Equals("all"))
                        {
                            shouldDie = true;
                        }
                    }
                }
                else if (splitargs[0] == "nvbGen_to_parser")
                {
                    if (splitargs.Length > 2)
                    {

                        string sentenceToBeParsed = string.Empty;
                        string charName = splitargs[1];
                        for (int i = 2; i < splitargs.Length; ++i)
                        {
                            sentenceToBeParsed += " " + splitargs[i];
                        }
                        sentenceToBeParsed = sentenceToBeParsed.Trim();
                        SendToParser(charName, sentenceToBeParsed);
                    }
                }
            }
        }

        private static void SendToParser(string charName, string sentenceToBeParsed)
        {
            //string pathForStandordParser = @"E:\Projects\Stanford Parser\stanford-parser-full-2014-01-04\stanford-parser-full-2014-01-04\";
            //string fileName = pathForStandordParser + @"testsent.txt";
            string fileName = @"testsent.txt";
            fileName = Path.GetFullPath(fileName);
            File.WriteAllText(fileName, sentenceToBeParsed);

            Console.WriteLine("StanfordParserWrapper: Sending to parser - \n" + fileName + ", " + sentenceToBeParsed);
            string outputFromParser = StartStanfordParser(fileName);
            Console.WriteLine("StanfordParserWrapper: Received from parser - \n" + outputFromParser);

            outputFromParser = CleanUpOutput(outputFromParser);
            Console.WriteLine("StanfordParserWrapper: After clean up - \n" + outputFromParser); 

            //If there cmdOptions is not null and should not use version2 (for cerebella), send parser_result
            if (cmdOptions != null && !cmdOptions.shouldUseVersion2)
            {
                vhmsg.SendMessage("parser_result " + charName + " " + outputFromParser);
            }
            //If cmdOptions is in fact null, default to sending parser_result
            else if (cmdOptions == null)
            {
                vhmsg.SendMessage("parser_result " + charName + " " + outputFromParser);
            }
            //if we do need to send parser_result2 (for cerebella)
            else if (cmdOptions.shouldUseVersion2)
            {
                string outputFromParser2 = CreateHierarchy(outputFromParser, sentenceToBeParsed);
                Console.WriteLine("StanfordParserWrapper: Output for parser2(cerebella compliant) - \n" + outputFromParser2);
                vhmsg.SendMessage("parser_result2 " + charName + " " + outputFromParser2);
            }
            //If everythin else fails, just send parser_result
            else
            {
                vhmsg.SendMessage("parser_result " + charName + " " + outputFromParser);
            }
        }

        private static string CreateHierarchy(string outputFromParser, string sentence)
        {
            string hierarchy = string.Empty;

            string[] separators = new string[1];
            separators[0] = "(";

            string[] splitParsedString = outputFromParser.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            bool[] splitParsedStringProcessed = new bool[splitParsedString.Length];

            for (int i = 0; i < splitParsedStringProcessed.Length; ++i)
            {
                splitParsedStringProcessed[i] = false;
            }

            List<TreeNode> nodes = new System.Collections.Generic.List<TreeNode>();

            CreateTreeHierarchy(nodes, splitParsedString, 0, 0, splitParsedStringProcessed);
            hierarchy = CreateStringFromHierarchyTree(nodes);
            hierarchy = outputFromParser + "\n" + hierarchy;
            return hierarchy;
        }

        private static string CreateStringFromHierarchyTree(System.Collections.Generic.List<TreeNode> nodes)
        {
            string hierarchyString = string.Empty;
            foreach (TreeNode node in nodes)
            {
                hierarchyString += "'" + node.wordType.ToString() + "'" + ", ";
                hierarchyString += "(" + "\"" + node.word + "\"" + ", ";
                hierarchyString += node.start.ToString() + ", ";
                hierarchyString += node.end.ToString() + ")";
                hierarchyString += "\r\n";
            }

            return hierarchyString;
        }

        private static void CreateTreeHierarchy(System.Collections.Generic.List<TreeNode> nodes, string[] splitParsedString, int splitParsedStringIndex, int start, bool[] splitParsedStringProcessed)
        {
            if (splitParsedStringIndex >= splitParsedString.Length) return;

            if (!String.IsNullOrEmpty(splitParsedString[splitParsedStringIndex]))
            {
                //this means it does contain a word
                string trimmedString = splitParsedString[splitParsedStringIndex].Trim();
                if (trimmedString.EndsWith(")"))
                {
                    //the first index of ) should be a word
                    int indexOfEndOfWord = trimmedString.IndexOf(")");
                    string[] space = new string[1];
                    space[0] = " ";
                    //the firt part should be syntax and 2nd part should be word
                    string[] syntaxAndWord = trimmedString.Split(space, StringSplitOptions.RemoveEmptyEntries);

                    if (syntaxAndWord.Length == 2)
                    {
                        TreeNode wordNode = new TreeNode();
                        wordNode.wordType = TreeNode.type.word;
                        wordNode.word = syntaxAndWord[1].Replace(")", "").Trim();
                        wordNode.start = start;
                        wordNode.end = start + 1;
                        nodes.Add(wordNode);

                        TreeNode syntaxNode = new TreeNode();
                        syntaxNode.wordType = TreeNode.type.syntax;
                        syntaxNode.word = syntaxAndWord[0].Trim();
                        syntaxNode.start = start;
                        syntaxNode.end = start + 1;
                        nodes.Add(syntaxNode);

                        splitParsedStringProcessed[splitParsedStringIndex] = true;

                        //now let's find the other end of syntax
                        for (int wordsFound = 0, j = 1, i = trimmedString.Length - 2; i > 0;++j)
                        {
                            if (trimmedString[i] == ')')
                            {
                                if (splitParsedStringIndex - j >= 0)
                                {
                                    //If this is not a word, syntax, then it should be synta
                                    if (!splitParsedString[splitParsedStringIndex - j].Trim().Contains(" "))
                                    {
                                        if (!splitParsedStringProcessed[splitParsedStringIndex - j])
                                        {
                                            string previousSyntax = splitParsedString[splitParsedStringIndex - j];
                                            TreeNode synNode = new TreeNode();
                                            synNode.wordType = TreeNode.type.syntax;
                                            synNode.word = previousSyntax.Trim();
                                            /*if (start - j < 0)
                                                synNode.start = 0;
                                            else
                                                synNode.start = start - j;*/
                                            synNode.start = start - wordsFound;
                                            synNode.end = start + 1;
                                            nodes.Add(synNode);
                                            --i;
                                            splitParsedStringProcessed[splitParsedStringIndex - j] = true;
                                        }
                                    }
                                    //otherwise it's a word which should be deducted from start
                                    else
                                    {
                                        ++wordsFound;
                                    }
                                }
                            }
                            else
                            {
                                break;
                            }
                        }

                        start = start + 1;

                    }
                }
                //else
                //{
                CreateTreeHierarchy(nodes, splitParsedString, splitParsedStringIndex + 1, start, splitParsedStringProcessed);
                //}

            }
            return;
        }

        class TreeNode
        {
            public enum type 
            {
                word,
                syntax
            };
            public type wordType;
            public string word;
            public int start;
            public int end;
        }

        private static string ReplaceFirst(string text, string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return text;
            }
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        private static string ReplaceLast(string text, string search, string replace)
        {
            int pos = text.LastIndexOf(search);
            if (pos < 0)
            {
                return text;
            }
            return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
        }

        ////Old cleanup method which doesnt take into consideration that there can be multiple outputs from the parser.
        ////Just takes first sentence output and cleans it up to create parser_result2
        //private static string CleanUpOutput(string outputFromParser)
        //{
        //    outputFromParser = outputFromParser.Substring(outputFromParser.IndexOf(".txt\"") + ".txt\"".Length);
        //    if (outputFromParser.Contains("\r\n\r\n"))
        //    {
        //        outputFromParser = outputFromParser.Substring(0, outputFromParser.IndexOf("\r\n\r\n"));
        //    }

        //    outputFromParser = outputFromParser.Replace("\r\n", " ").Trim();

        //    return outputFromParser;
        //}

        //New Cleanup method which takes into consideration that the output from Stanford parser might contain multiple sentences.
        //This generally happens when the sentence contains a period ".", or exclamation "!"
        //It uses a recursive method to clean up the output and concatenate the output
        private static string CleanUpOutput(string outputFromParser)
        {
            //Remove the initial console part
            /*"..\..\bin\java\jre\bin\jav
            a.exe" -mx150m -cp "stanford-parser.jar;" edu.stanford.nlp.parser.lexparser.Lexi
            calizedParser -outputFormat "penn,typedDependencies" englishPCFG.ser.gz "E:\Proj
            ects\VHToolkit\svn_latest\bin\StanfordParserWrapper\testsent.txt"
             * */
            outputFromParser = outputFromParser.Substring(outputFromParser.IndexOf(".txt\"") + ".txt\"".Length);
            outputFromParser = RecursiveCleanUp(outputFromParser, 0);

            return outputFromParser;
        }

        //Recursive cleanup method
        private static string RecursiveCleanUp(string outputFromParser, int sentenceNumber)
        {
            string tempOutput = outputFromParser;
            //Check if output contains \r\n\r\n
            if (outputFromParser.Contains("\r\n\r\n"))
            {
                outputFromParser = outputFromParser.Substring(0, outputFromParser.IndexOf("\r\n\r\n"));

                tempOutput = tempOutput.Substring(outputFromParser.Length);
            }
            //If \r\n\r\n is not present, there is more output, return empty string
            else
            {
                return string.Empty;
            }

            //Replace all \r\n by " "
            outputFromParser = outputFromParser.Replace("\r\n", " ").Trim();
            
            //Remove the last ")" and add it in the end
            if (outputFromParser.Trim().EndsWith(")"))
            {
                outputFromParser = ReplaceLast(outputFromParser, ")", "");
            }

            //This means there is another sentence as output, get its output and append it to our current output
            //Note that we are removing the (ROOT here which means above we remove the matching ) at the end
            if (tempOutput.Contains("(ROOT\r\n"))
            {
                //Remove "(ROOT\r\n"
                tempOutput = tempOutput.Substring(tempOutput.IndexOf("(ROOT\r\n"));
                tempOutput = ReplaceFirst(tempOutput, "(ROOT\r\n", "");

                //Recursively call this method and append to the output
                outputFromParser += " " + RecursiveCleanUp(tempOutput, sentenceNumber + 1);

            }

            //if it's the first sentence, add the ending ")"
            if (sentenceNumber == 0)
            {
                outputFromParser = outputFromParser + ")";
            }

            return outputFromParser;
        }

        public static void MessageLoop()
        {
            // initializing timer value
            int timer_millisec = System.DateTime.Now.Millisecond;
            const int fps_limit = 100;

            while (!shouldDie)
            {
                System.Windows.Forms.Application.DoEvents();


                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo c = Console.ReadKey();
                    if (c.KeyChar.Equals('q'))
                        shouldDie = true;
                }


                // code to limit the CPU usage as otherwise it takes up the complete CPU
                // Currently limiting to 60 frames per second. Might not be accurately 60 due to granularity issues.
                int timesincelastframe = System.DateTime.Now.Millisecond - timer_millisec;

                int ttW;
                ttW = (1000 / fps_limit) - timesincelastframe;
                if (ttW > 0)
                    Thread.Sleep(ttW);

                timer_millisec = System.DateTime.Now.Millisecond;
            }

            CleanUp();
        }

        private static void CleanUp()
        {
            // remnants from charniak parser code.  unsure if needed
            vhmsg.SendMessage("elvinSim_to_parser release");
            vhmsg.SendMessage("elvinSim_to_nvbGen release");

            vhmsg.SendMessage("vrProcEnd nvb parser");
            vhmsg.CloseConnection();
        }
    }
}
