using System;
using System.IO;
using System.Xml.Serialization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommandLine;

// TODO: Launch the first new image for each thing updated?

namespace frontline
{
    class Program
    {
        class Options
        {
            [Option('s', "subscriptions", Required = true, HelpText = "Specify the subscriptions xml to process and update.")]
            public string XmlFilePath { get; set; }
            [Option('v', "verbose", Default = false, Required = false, HelpText = "Write out extra info about each download taking place.")]
            public bool Verbose { get; set; }
            [Option('o', "open", Default = false, Required = false, HelpText = "Open the first update for each feed with the default program for that file.")]
            public bool OpenUpdates { get; set; }
            [Option('p', "program", Default = "", Required = false, HelpText = "Program to open files with. Otherwise uses default application.")]
            public string Program { get; set; }
        }
        public static List<string> helpStrings = new List<string>{ "help", "?" };
        static readonly HttpClient httpClient = new HttpClient();
        static readonly WebClient webClient = new WebClient();
        static Subscriptions subscriptions = null;
        static int updatedSubscriptions = 0;
        static int updatedFiles = 0;
        static List<string> updatesToOpen = new List<string>();
        static Options options;

        static int Main(string[] args)
        {
            return CommandLine.Parser.Default.ParseArguments<Options>(args)
                .MapResult(opts => { options = opts; return RunProgram(); }, _ => 1);
        }

        private static int RunProgram()
        {
            if (!File.Exists(options.XmlFilePath))
            {
                Console.Error.WriteLine("Error: Subscription file \"{0}\" not found.", options.XmlFilePath);
                return 1;
            }

            try
            {
                using (StreamReader reader = new StreamReader(options.XmlFilePath))
                    subscriptions = (Subscriptions)new XmlSerializer(typeof(Subscriptions)).Deserialize(reader);

                foreach (var sub in subscriptions.Infos)
                {
                    if (options.Verbose)
                        Console.WriteLine("Updating subscription \"{0}\" (which is type \"{1}\")", sub.GetName(), nameof(sub));
                    RunSubscription(sub);
                }

            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            Console.WriteLine("Updated {0} subscriptions with {1} files.", updatedSubscriptions, updatedFiles);

            bool success = OpenUpdates();
            SaveResults();

            return success ? 0 : 1;
        }

        static bool RunSubscription(SubscriptionInfo sub)
        {
            UpdateResult result;
            bool updated = false;
            while (true)
            {
                result = GetUpdate(sub).GetAwaiter().GetResult();
                if (result != UpdateResult.ContentFound)
                    break;

                if (!updated && options.OpenUpdates)
                    updatesToOpen.Add(sub.GetLocalPathWithoutExt());

                sub.Increment();
                ++updatedFiles;
                updated = true;
            }

            if (updated)
                ++updatedSubscriptions;

            if (result == UpdateResult.Error)
                Console.Error.WriteLine("Encountered an error while updating feed \"{0}\".", sub.GetName());

            return result == UpdateResult.UpToDate;
        }
        enum UpdateResult
        {
            UpToDate,
            ContentFound,
            Error
        }
        static async Task<UpdateResult> GetUpdate(SubscriptionInfo sub)
        {
            try
            {
                var response = await httpClient.GetAsync(sub.GetNextPageUrl());
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Console.WriteLine("No page yet for {0}. Stopping...", sub.GetNextPageUrl());
                        return UpdateResult.UpToDate;
                    }

                    Console.Error.WriteLine("Failed to get content at url {0}. With status code {1}, reason \"{2}\" ", sub.GetNextPageUrl(), response.StatusCode, response.ReasonPhrase);
                    return UpdateResult.Error;
                }

                string body = await response.Content.ReadAsStringAsync();
                SubscriptionInfo.ContentInfo imageInfo = sub.FindImage(body);

                string path = Path.Combine(subscriptions.SaveDir, imageInfo.localFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                if (options.Verbose)
                    Console.WriteLine("Downloading image from {0} and saving to \"{1}\".", imageInfo.imageURL, path);

                webClient.DownloadFile(imageInfo.imageURL, path);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return UpdateResult.Error;
            }
            return UpdateResult.ContentFound;
        }
        static bool OpenUpdates()
        {
            bool success = true;
            foreach (var localPath in updatesToOpen)
            {
                // Find the file with its extension.
                var files = Directory.GetFiles(subscriptions.SaveDir, localPath + ".*");
                if (files.Length == 0)
                {
                    Console.Error.WriteLine("Exptected to find file for \"{0}\", but none was found.", localPath);
                    continue;
                }
                else if (files.Length > 1)
                {
                    Console.WriteLine("Warning: Expected to find 1 file for \"{0}\", found {1}. (Are there files with different extensions?)", localPath, files.Length);
                    Console.WriteLine("Only opening files \"{0}\".", files[0]);
                }
                var file = files[0];
                if (options.Verbose)
                    Console.WriteLine("Opening file \"{0}\".", file);

                try
                {
                    var process = new System.Diagnostics.Process();
                    if (options.Program.Length > 0)
                    {
                        process.StartInfo = new System.Diagnostics.ProcessStartInfo(
                            Environment.ExpandEnvironmentVariables(options.Program),
                            file)
                        { UseShellExecute = true };
                    }
                    else
                    {
                        process.StartInfo = new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true };
                    }
                    process.Start();
                }
                catch (Exception e)
                {
                    if (options.Program.Length > 0)
                        Console.Error.WriteLine("Failed to open file \"{0}\" with program \"{1}\". {2}", file, options.Program, e);
                    else
                        Console.Error.WriteLine("Failed to open file \"{0}\". {1}", file, options.Program, e);
                    success = false;
                }
            }
            return success;
        }
        static void SaveResults()
        {
            if (subscriptions != null && updatedFiles > 0)
            {
                string tempFile = options.XmlFilePath + ".tmp";
                using (StreamWriter writer = new StreamWriter(tempFile))
                    new XmlSerializer(typeof(Subscriptions)).Serialize(writer, subscriptions);
                File.Delete(options.XmlFilePath);
                File.Move(tempFile, options.XmlFilePath);
            }
        }
    }
}
