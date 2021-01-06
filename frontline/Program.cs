using System;
using System.IO;
using System.Xml.Serialization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using CommandLine;
using System.Threading;

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
        static readonly HttpClient httpClient = new HttpClient();
        static readonly WebClient webClient = new WebClient();
        static Subscriptions subscriptions = null;
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

            var tasks = new List<Task<(bool, int, string)>>();
            try
            {
                using (StreamReader reader = new StreamReader(options.XmlFilePath))
                    subscriptions = (Subscriptions)new XmlSerializer(typeof(Subscriptions)).Deserialize(reader);

                foreach (var sub in subscriptions.Infos)
                {
                    if (options.Verbose)
                        Console.WriteLine("Updating subscription \"{0}\" (which is type \"{1}\")", sub.GetName(), sub.GetType());
                    tasks.Add(RunSubscription(sub));
                }

                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }

            int errorsEncountered = 0;
            int updatedSubscriptions = 0;
            int updatedFiles = 0;
            var filesToOpen = new List<string>();

            foreach (var task in tasks)
            {
                var (success, filesUpdated, fileToOpen) = task.Result;
                if (!success)
                    ++errorsEncountered;
                updatedFiles += filesUpdated;
                if (fileToOpen != null)
                {
                    filesToOpen.Add(fileToOpen);
                    ++updatedSubscriptions;
                }
            }

            Console.WriteLine($"Updated {updatedSubscriptions} subscriptions with {updatedFiles} files." );

            if (errorsEncountered != 0)
                Console.Error.WriteLine($"Encountered {errorsEncountered} errors." );

            if (updatedFiles > 0)
                SaveResults();

            return OpenFiles(filesToOpen) ? 0 : 1;
        }

        static async Task<(bool, int, string)> RunSubscription(SubscriptionInfo sub)
        {
            UpdateResult result;
            string firstNewFile = null;
            int filesUpdated = 0;
            while (true)
            {
                result = await TryGetNextPage(sub);
                if (result != UpdateResult.ContentFound)
                    break;

                if (firstNewFile == null && options.OpenUpdates)
                    firstNewFile = sub.GetLocalPathWithoutExt();

                sub.Increment();
                ++filesUpdated;
            }

            if (result == UpdateResult.Error)
                Console.Error.WriteLine("Encountered an error while updating feed \"{0}\".", sub.GetName());

            return (result == UpdateResult.UpToDate, filesUpdated, firstNewFile);
        }

        enum UpdateResult
        {
            UpToDate,
            ContentFound,
            Error
        }

        static async Task<UpdateResult> TryGetNextPage(SubscriptionInfo sub)
        {
            try
            {
                var response = await httpClient.GetAsync(sub.GetNextPageUrl());
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Console.WriteLine($"No page yet for {sub.GetNextPageUrl()}. Stopping...");
                        return UpdateResult.UpToDate;
                    }

                    Console.Error.WriteLine($"Failed to get content at url {sub.GetNextPageUrl()}. With status code {response.StatusCode}, reason \"{response.ReasonPhrase}\".");
                    return UpdateResult.Error;
                }

                string body = await response.Content.ReadAsStringAsync();
                SubscriptionInfo.ContentInfo imageInfo = sub.FindImage(body);

                string path = Path.Combine(subscriptions.SaveDir, imageInfo.localFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                if (options.Verbose)
                    Console.WriteLine($"Downloading image from {imageInfo.imageURL} and saving to \"{path}\".");

                webClient.DownloadFile(imageInfo.imageURL, path);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return UpdateResult.Error;
            }
            return UpdateResult.ContentFound;
        }

        static bool OpenFiles(List<string> updatedFiles)
        {
            bool success = true;
            foreach (var localPath in updatedFiles)
            {
                // Find the file with its extension.
                var files = Directory.GetFiles(subscriptions.SaveDir, localPath + ".*");
                if (files.Length == 0)
                {
                    Console.Error.WriteLine($"Exptected to find file for \"{localPath}\", but none was found.");
                    continue;
                }
                else if (files.Length > 1)
                {
                    Console.WriteLine($"Warning: Expected to find 1 file for \"{localPath}\", found {files.Length}. (Are there files with different extensions?)");
                    Console.WriteLine($"Only opening file \"{files[0]}\".");
                }
                var file = files[0];
                if (options.Verbose)
                    Console.WriteLine($"Opening file \"{file}\".");

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
                        Console.Error.WriteLine($"Failed to open file \"{file}\" with program \"{options.Program}\". {e}");
                    else
                        Console.Error.WriteLine($"Failed to open file \"{file}\". {e}");
                    success = false;
                }
            }
            return success;
        }

        static void SaveResults()
        {
            if (subscriptions == null)
                return;

            string tempFile = options.XmlFilePath + ".tmp";
            using (StreamWriter writer = new StreamWriter(tempFile))
                new XmlSerializer(typeof(Subscriptions)).Serialize(writer, subscriptions);
            File.Delete(options.XmlFilePath);
            File.Move(tempFile, options.XmlFilePath);
        }
    }
}
