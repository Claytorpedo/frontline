using System;
using System.IO;
using System.Xml.Serialization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

// TODO: Launch the first new image for each thing updated?

namespace frontline
{
    class Program
    {
        public static List<string> helpStrings = new List<string>{ "help", "?" };
        static readonly HttpClient httpClient = new HttpClient();
        static readonly WebClient webClient = new WebClient();
        static Subscriptions subscriptions = null;
        static int updatedSubscriptions = 0;
        static int updatedFiles = 0;

        static void Main(string[] args)
        {
            if (args.Length == 0 || helpStrings.Contains(args[0]))
            {
                Console.WriteLine("----------------------------------------------------");
                Console.WriteLine("frontline");
                Console.WriteLine("\nPlease supply the subscriptions file.\nUsage: frontline <path/to/my/subscriptions.xml>");
                Console.WriteLine("----------------------------------------------------");
                return;
            }
            string xmlFilePath = args[0];
            if (!File.Exists(xmlFilePath))
            {
                Console.WriteLine("Error: Subscription file \"{0}\" not found.", xmlFilePath);
                return;
            }

            try
            {
                using (StreamReader reader = new StreamReader(xmlFilePath))
                    subscriptions = (Subscriptions)new XmlSerializer(typeof(Subscriptions)).Deserialize(reader);

                foreach (var sub in subscriptions.Infos)
                {
                    Console.WriteLine("Updating subscription \"{0}\" (which is type \"{1}\")", sub.GetName(), nameof(sub));
                    RunSubscription(sub);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            if (subscriptions != null && updatedFiles > 0)
            {
                string tempFile = xmlFilePath + ".tmp";
                using (StreamWriter writer = new StreamWriter(tempFile))
                    new XmlSerializer(typeof(Subscriptions)).Serialize(writer, subscriptions);
                File.Delete(xmlFilePath);
                File.Move(tempFile, xmlFilePath);
            }

            Console.WriteLine("Updated {0} subscriptions with {1} files.", updatedSubscriptions, updatedFiles);
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
                sub.Increment();
                ++updatedFiles;
                updated = true;
            }

            if (updated)
                ++updatedSubscriptions;

            if (result == UpdateResult.Error)
                Console.WriteLine("Encountered an error while updating feed \"{0}\".", sub.GetName());

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

                    Console.WriteLine("Failed to get content at url {0}. With status code {1}, reason \"{2}\" ", sub.GetNextPageUrl(), response.StatusCode, response.ReasonPhrase);
                    return UpdateResult.Error;
                }

                string body = await response.Content.ReadAsStringAsync();
                SubscriptionInfo.ContentInfo imageInfo = sub.FindImage(body);

                string path = Path.Combine(subscriptions.SaveDir, imageInfo.localFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                Console.WriteLine("Downloading image from {0} and saving to \"{1}\".", imageInfo.imageURL, path);

                webClient.DownloadFile(imageInfo.imageURL, path);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return UpdateResult.Error;
            }
            return UpdateResult.ContentFound;
        }
    }
}
