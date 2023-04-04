using System.IO;
using System.Xml.Serialization;
using System.Text.RegularExpressions;

namespace frontline
{
    public abstract class SubscriptionInfo
    {
        public const string ImageExtensions = "png|jpg|jpeg";
        public abstract string GetNextPageUrl();
        public struct ContentInfo
        {
            public string imageURL;
            public string localFilePath;
        }

        public abstract ContentInfo GetCover();
        public abstract ContentInfo FindImage(string pageBody);
        public abstract string GetName();
        public abstract string GetLocalFileNameNoExt();
        public abstract void Increment();
        public virtual string GetLocalPathWithoutExt() { return Path.Combine(GetName(), GetLocalFileNameNoExt()); }
    }
    public class SaizensenInfo : SubscriptionInfo
    {
        public override string GetNextPageUrl()
        {
            return baseLink + GetLocalFileNameNoExt() + ".html";
        }
        public override ContentInfo GetCover()
        {
            var info = new ContentInfo();
            info.imageURL = Path.Combine(baseLink, "res/images/cover.png");
            info.localFilePath = Path.Combine(GetName(), "_cover.png");
            return info;
        }
        public override ContentInfo FindImage(string pageBody)
        {
            var info = new ContentInfo();
            // Link format:           <baselink><folders and comic name><page#>.<an id of some kind>.<img ext -- always png?>
            var expr = string.Format("{0}[a-z0-9-\\/\\.]+?{1}[a-zA-Z0-9-\\/\\.]*?\\.({2})", Regex.Escape(baseLink), GetLocalFileNameNoExt(), ImageExtensions);
            var match = Regex.Match(pageBody, expr, RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new InvalidDataException(string.Format("Failed to find regex match for page {0} of {1}. Has the format changed?", pageIndex, name));
            info.imageURL = match.Value;
            var ext = info.imageURL.Substring(info.imageURL.LastIndexOf('.'));
            info.localFilePath = GetLocalPathWithoutExt() + ext;
            return info;
        }
        public override string GetName()
        {
            return name;
        }
        public override string GetLocalFileNameNoExt()
        {
            return pageIndex.ToString("D" + pageNumPadding);
        }
        public override void Increment()
        {
            ++pageIndex;
        }

        public string baseLink;
        public string name;
        public int pageIndex;
        public int pageNumPadding;
    }

    public class SaizensenInfoV2 : SubscriptionInfo
    {
        public override string GetNextPageUrl()
        {
            return domain + domainSubfolder + GetLocalFileNameNoExt() + ".html";
        }
        public override ContentInfo GetCover()
        {
            var info = new ContentInfo();
            info.imageURL = domain + Path.Combine(domainSubfolder, "res/images/cover.png");
            info.localFilePath = Path.Combine(GetName(), "_cover.png");
            return info;
        }
        public override ContentInfo FindImage(string pageBody)
        {
            var info = new ContentInfo();
            // Link format:           "/<domainSubfolder>/<more subfolders>/<page#>.<an id of some kind>.<img ext>"
            var expr = string.Format("\"{0}[a-zA-Z0-9-\\/\\.]+?{1}\\.[a-zA-Z0-9-]*?\\.({2})\"", Regex.Escape(domainSubfolder), GetLocalFileNameNoExt(), ImageExtensions);
            var match = Regex.Match(pageBody, expr, RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new InvalidDataException(string.Format("Failed to find regex match for page {0} of {1}. Has the format changed?", pageIndex, name));
            info.imageURL = domain + match.Value.Trim('"');
            var ext = info.imageURL.Substring(info.imageURL.LastIndexOf('.'));
            info.localFilePath = GetLocalPathWithoutExt() + ext;
            return info;
        }
        public override string GetName()
        {
            return name;
        }
        public override string GetLocalFileNameNoExt()
        {
            return pageIndex.ToString("D" + pageNumPadding);
        }
        public override void Increment()
        {
            ++pageIndex;
        }

        public string domain;
        public string domainSubfolder;
        public string name;
        public int pageIndex;
        public int pageNumPadding;
    }
    public class Subscriptions
    {
        public string SaveDir;
        [XmlArrayItem(typeof(SaizensenInfo)), XmlArrayItem(typeof(SaizensenInfoV2))]
        public SubscriptionInfo[] Infos;
    }
}
