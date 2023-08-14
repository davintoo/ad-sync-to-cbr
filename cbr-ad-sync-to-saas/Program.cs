using System;
using System.Configuration;
using System.DirectoryServices;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Net;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml;

namespace cbr_ad_sync_to_saas
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();

        const string UPLOAD_URI = "/api/v2/users-import/csv";
        const string UPLOAD_PHOTO_URI = "/api/v2/users-import/update-photo";
        const string BROWSER_ID = "cbr-ad-sync-tool";

        const string PHOTOS_CACHE_DIR = "photos";

        const string APP_NAME = "Collaborator AD sync";

        readonly static string[] AD_PROPS_TO_LOAD = { "samaccountname", "L", "Department", "Title", "telephoneNumber", "objectguid", "givenname", "cn", "sn", "mail", "UserPrincipalName", "C" };

        static void Main(string[] args)
        {
            try
            {
                string customConfigPath = "";
                if (args.Length > 0)
                {
                    foreach (string arg in args)
                    {
                        if (arg.Contains("--config"))
                        {
                            customConfigPath = arg.Replace("--config=", String.Empty);
                            Console.WriteLine("Use config from " + customConfigPath);

                            var xmlDoc = new XmlDocument();
                            xmlDoc.Load(customConfigPath);
                            foreach(string key in ConfigurationManager.AppSettings.AllKeys)
                            {
                                var xmlVal = xmlDoc.SelectSingleNode("//appSettings//add[@key='"+ key + "']");
                                if(xmlVal != null)
                                {
                                    ConfigurationManager.AppSettings[key] = xmlVal.Attributes["value"].Value;
                                }
                            }
                        }
                    }
                }
               
                bool saveLocal = ConfigurationManager.AppSettings["ad-save-local"] == "true";
                bool debugAd = false;
                List<string> photosForUpload = new List<string>();
                bool syncPhotos = ConfigurationManager.AppSettings["ad-sync-photos"] == "true";
                bool appendMode = ConfigurationManager.AppSettings["ad-append-mode"] == "true";
                if (args.Length > 0)
                {
                    foreach (string arg in args)
                    {
                        if (arg == "--save-local")
                        {
                            saveLocal = true;
                        }
                        if (arg == "--debug-ad")
                        {
                            debugAd = true;
                        }
                        if (arg == "--append-mode")
                        {
                            appendMode = true;
                        }
                    }
                }
                Console.WriteLine("Start");
                Console.WriteLine("Read data from AD ...");

                List<string> items = new List<string>();
                if(!appendMode)
                {
                    items.Add("ID;Фамилия;Имя;Отчество;Логин;Почта;Пароль;Дата рождения;Пол (Ж-1, М-0);Город;Подразделение;Должность;Метки;телефон");
                }

                string[] ldapConnStrings = ConfigurationManager.AppSettings["ad-server"].Split(';');
                foreach (string ldapConnString in ldapConnStrings)
                {
                    getLdapData(items, photosForUpload, ldapConnString, debugAd);
                }


                if (debugAd)
                {
                    return;
                }

                if (syncPhotos)
                {
                    Console.WriteLine("Need update photos: " + photosForUpload.Count);
                }

                Console.WriteLine("AD done. Found " + items.Count + " items");

                if (saveLocal)
                {
                    string outFileName = "rusers.csv";
                    Console.WriteLine("Save to file " + outFileName);
                    //String.Join("\n", items.ToArray())

                    StreamWriter file = new StreamWriter(Directory.GetCurrentDirectory() + "/" + outFileName, appendMode);
                    file.WriteLine(String.Join("\n", items.ToArray()));
                    file.Close();

                    return;
                }
                
                Console.WriteLine("Upload csv file to the server ...");

                //byte[] utf8bytes = Encoding.Default.GetBytes(String.Join("\n", items.ToArray()));
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

                Console.WriteLine(uploadFile(ConfigurationManager.AppSettings["cbr-server"] + UPLOAD_URI,
                    ConfigurationManager.AppSettings["cbr-token"],
                    Encoding.UTF8.GetBytes(String.Join("\n", items.ToArray())),
                    "file.csv", "text/csv"));

                Console.WriteLine("Upload csv file done");

                if (syncPhotos)
                {
                    Console.WriteLine("Upload photos ...");
                    if (!Directory.Exists(Directory.GetCurrentDirectory() + "/" + PHOTOS_CACHE_DIR))
                    {
                        Directory.CreateDirectory(Directory.GetCurrentDirectory() + "/" + PHOTOS_CACHE_DIR);
                    }
                    foreach (string uid in photosForUpload)
                    {
                        string photoPath = Directory.GetCurrentDirectory() + "/" + PHOTOS_CACHE_DIR + "/" + uid + ".jpg";
                        var photoUploadRes = uploadFile(ConfigurationManager.AppSettings["cbr-server"] + UPLOAD_PHOTO_URI, ConfigurationManager.AppSettings["cbr-token"],
                            File.ReadAllBytes(photoPath), uid + ".jpg", "image/jpeg", uid);
                    }
                    Console.WriteLine("Upload photos done");
                }
                
                Console.WriteLine("All done");

                //Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.ToString());

                try
                {
                    if (!EventLog.SourceExists(APP_NAME))
                    {
                        EventLog.CreateEventSource(APP_NAME, "Application");
                    }
                    EventLog.WriteEntry(APP_NAME, ex.ToString(), EventLogEntryType.Error, 101);
                }
                catch (Exception innerEx)
                { }


                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void getLdapData(List<string> items, List<string> photosForUpload, string ldapConnStr, bool debugAd)
        {
            bool syncPhotos = ConfigurationManager.AppSettings["ad-sync-photos"] == "true";
            List<string> item = new List<string>();
            DirectoryEntry ldapConnection = new DirectoryEntry(
                   ldapConnStr,
                   ConfigurationManager.AppSettings["ad-username"],
                   ConfigurationManager.AppSettings["ad-password"]);

            DirectorySearcher search = new DirectorySearcher(ldapConnection);
            search.PageSize = 200;
            search.Filter = ConfigurationManager.AppSettings["ad-filter"];
            SearchResultCollection results = search.FindAll();
            bool extractTags = ConfigurationManager.AppSettings["ad-extract-tags"] != null;
            //List<string> groups = new List<string>();
            List<string> tags = new List<string>();
            Dictionary<string, List<string>> tagsFields = new Dictionary<string, List<string>>();
            if (extractTags)
            {
                string[] pairs = ConfigurationManager.AppSettings["ad-extract-tags"].Split(';');
                foreach (string pair in pairs)
                {
                    string[] tmp = pair.Split('=');
                    tagsFields[tmp[0]] = new List<string>(tmp[1].Split(','));
                }
            }

            if (results.Count > 0)
            {
                foreach (SearchResult result in results)
                {
                    if (result.Properties["samaccountname"].Count == 0)
                    {
                        continue;
                    }

                    if (debugAd)
                    {
                        foreach (string propertyName in result.Properties.PropertyNames)
                        {
                            if (result.Properties[propertyName].Count > 1)
                            {
                                Console.Write(propertyName + ": ");
                                foreach (var pv in result.Properties[propertyName])
                                {
                                    Console.Write(pv.ToString() + ",");
                                }
                                Console.WriteLine("");
                            }
                            else
                            {
                                Console.WriteLine(propertyName + ": " + (result.Properties[propertyName].Count > 0 ?
                                    result.Properties[propertyName][0].ToString() : ""));
                            }
                        }

                        Console.WriteLine("===================");
                    }

                    Guid id = new Guid((byte[])result.Properties["objectguid"][0]);
                    item = new List<string>();
                    item.Add(id.ToString());//id
                    item.Add(retrieveADProperty(result, "sn"));//secondname
                    string givenname = retrieveADProperty(result, "givenname");//firstname
                    if (String.IsNullOrEmpty(givenname))
                    {
                        givenname = retrieveADProperty(result, "cn");//firstname
                    }
                    item.Add(givenname);//lastname
                    item.Add("");//patronymics
                    item.Add(result.Properties["samaccountname"][0].ToString());//login
                    string mail = retrieveADProperty(result, "mail");//email
                    if (String.IsNullOrEmpty(mail))
                    {
                        mail = retrieveADProperty(result, "userprincipalname");//email
                        if (String.IsNullOrEmpty(mail))
                        {
                            mail = retrieveADProperty(result, "samaccountname") + ConfigurationManager.AppSettings["ad-email-sufix"];//email
                        }
                    }
                    item.Add(mail);//email   
                    item.Add(Guid.NewGuid().ToString());//password
                    item.Add("");//birth day
                    item.Add("");//gender
                    item.Add(retrieveADProperty(result, "L") + "-" + retrieveADProperty(result, "C"));//city                        
                    item.Add(retrieveADProperty(result, "Department"));//department                       
                    item.Add(retrieveADProperty(result, "title"));//position


                    if (extractTags)
                    {
                        tags = new List<string>();
                        string val;
                        foreach (string field in tagsFields.Keys)
                        {
                            val = retrieveADProperty(result, field);
                            foreach (string pair in val.Split(','))
                            {
                                string[] tmp = pair.Split('=');
                                if (tagsFields[field].Contains(tmp[0]) && !tags.Contains(tmp[1]))
                                {
                                    tags.Add(tmp[1]);
                                }
                            }
                        }
                        item.Add(String.Join(",", tags.ToArray()));//tags
                    }
                    else
                    {
                        item.Add("");//tags
                    }

                    item.Add(retrieveADProperty(result, "telephoneNumber"));//phone
                    items.Add(String.Join(";", item.ToArray()));

                    if (syncPhotos && result.Properties["thumbnailPhoto"].Count > 0)
                    {
                        bool isImageChanged = saveImage(id.ToString(), result);
                        if (isImageChanged)
                        {
                            photosForUpload.Add(id.ToString());
                        }
                    }
                }
            }

            ldapConnection.Close();
        }

        private static string getImageHash(byte[] byteArray)
        {
            string hash;
            using (SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider())
            {
                hash = Convert.ToBase64String(sha1.ComputeHash(byteArray));
            }
            return hash;
        }

        private static bool saveImage(string uid, SearchResult searchResult)
        {
            bool isImageChanged = false;
            var photoData = searchResult.Properties["thumbnailPhoto"][0] as byte[];
            if (photoData != null)
            {
                string updatedHash = getImageHash(photoData);
                string originHash = updatedHash;
                string path = Directory.GetCurrentDirectory() + "/" + PHOTOS_CACHE_DIR + "/" + uid + ".jpg";
                if (!File.Exists(path))
                {
                    isImageChanged = true;
                    File.Create(path).Dispose();
                }
                else
                {
                    originHash = getImageHash(File.ReadAllBytes(path));
                    isImageChanged = updatedHash != originHash;
                }

                if (isImageChanged)
                {
                    //Console.WriteLine("Photo " + uid + " originHash:" + originHash + ", updatedHash:" + updatedHash);
                    using (FileStream fs = new FileStream(path, FileMode.Open))
                    {
                        var wr = new BinaryWriter(fs);
                        wr.Write(photoData);
                        wr.Close();
                    }
                }
            }

            return isImageChanged;
        }

        private static string retrieveADProperty(SearchResult searchResult, string propertyName)
        {
            string result = String.Empty;
            //check if property exists
            if (searchResult.Properties[propertyName].Count > 1)
            {
                List<string> values = new List<string>();
                foreach (var ps in searchResult.Properties[propertyName])
                {
                    values.Add(ps.ToString());
                }
                result = String.Join(",", values.ToArray());
            }
            else if (searchResult.Properties[propertyName].Count > 0)
            {
                result = searchResult.Properties[propertyName][0].ToString();//retrieving properties
            }

            return result;
        }

        private static string uploadFile(string actionUrl, string authToken, byte[] fileContent, string fileName, string fileMimeType, string uid = null)
        {
            Dictionary<string, object> postParameters =
                new Dictionary<string, object>();
            if (uid != null)
            {
                postParameters.Add("uid", uid);
            }
            postParameters.Add("file",
                new FormUpload.FileParameter(fileContent, fileName, fileMimeType));

            // Create request and receive response
            HttpWebResponse webResponse =
                FormUpload.MultipartFormDataPost(actionUrl, authToken, BROWSER_ID, postParameters);

            string fullResponse = String.Empty;
            if (webResponse != null)
            {
                // Process response
                StreamReader responseReader = new StreamReader(webResponse.GetResponseStream());
                fullResponse = responseReader.ReadToEnd();
                webResponse.Close();
                //Response.Write(fullResponse);
            }

            return fullResponse;
        }
    }

    // Implements multipart/form-data POST in C# http://www.ietf.org/rfc/rfc2388.txt
    // http://www.briangrinstead.com/blog/multipart-form-post-in-c
    public static class FormUpload
    {
        private static readonly Encoding encoding = Encoding.UTF8;
        public static HttpWebResponse MultipartFormDataPost(string postUrl, string authToken, string userAgent, Dictionary<string, object> postParameters)
        {
            string formDataBoundary = String.Format("----------{0:N}", Guid.NewGuid());
            string contentType = "multipart/form-data; boundary=" + formDataBoundary;

            byte[] formData = GetMultipartFormData(postParameters, formDataBoundary);

            return PostForm(postUrl, authToken, userAgent, contentType, formData);
        }

        private static HttpWebResponse PostForm(string postUrl, string authToken, string userAgent, string contentType, byte[] formData)
        {
            HttpWebRequest request = WebRequest.Create(postUrl) as HttpWebRequest;

            if (request == null)
            {
                throw new NullReferenceException("request is not a http request");
            }

            // Set up the request properties.
            request.Method = "POST";
            request.ContentType = contentType;
            request.UserAgent = userAgent;
            //request.CookieContainer = new CookieContainer();
            request.ContentLength = formData.Length;
            request.Timeout = 60 * 60 * 1000;
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Add("Pragma", "no-cache");
            request.Headers.Add("X-Cbr-Authorization", "Bearer " + authToken);
            // Send the form data to the request.
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(formData, 0, formData.Length);
                requestStream.Close();
            }

            try
            {
                return request.GetResponse() as HttpWebResponse;
            }
            catch (WebException ex)
            {
                Console.WriteLine("Error: " + ex.ToString());
                using (var stream = ex.Response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    Console.WriteLine(reader.ReadToEnd());
                }
            }

            return null;
        }

        private static byte[] GetMultipartFormData(Dictionary<string, object> postParameters, string boundary)
        {
            Stream formDataStream = new System.IO.MemoryStream();
            bool needsCLRF = false;

            foreach (var param in postParameters)
            {
                // Thanks to feedback from commenters, add a CRLF to allow multiple parameters to be added.
                // Skip it on the first parameter, add it to subsequent parameters.
                if (needsCLRF)
                    formDataStream.Write(encoding.GetBytes("\r\n"), 0, encoding.GetByteCount("\r\n"));

                needsCLRF = true;

                if (param.Value is FileParameter)
                {
                    FileParameter fileToUpload = (FileParameter)param.Value;

                    // Add just the first part of this param, since we will write the file data directly to the Stream
                    string header = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\"\r\nContent-Type: {3}\r\n\r\n",
                        boundary,
                        param.Key,
                        fileToUpload.FileName ?? param.Key,
                        fileToUpload.ContentType ?? "application/octet-stream");

                    formDataStream.Write(encoding.GetBytes(header), 0, encoding.GetByteCount(header));

                    // Write the file data directly to the Stream, rather than serializing it to a string.
                    formDataStream.Write(fileToUpload.File, 0, fileToUpload.File.Length);
                }
                else
                {
                    string postData = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}",
                        boundary,
                        param.Key,
                        param.Value);
                    formDataStream.Write(encoding.GetBytes(postData), 0, encoding.GetByteCount(postData));
                }
            }

            // Add the end of the request.  Start with a newline
            string footer = "\r\n--" + boundary + "--\r\n";
            formDataStream.Write(encoding.GetBytes(footer), 0, encoding.GetByteCount(footer));

            // Dump the Stream into a byte[]
            formDataStream.Position = 0;
            byte[] formData = new byte[formDataStream.Length];
            formDataStream.Read(formData, 0, formData.Length);
            formDataStream.Close();

            return formData;
        }

        public class FileParameter
        {
            public byte[] File { get; set; }
            public string FileName { get; set; }
            public string ContentType { get; set; }
            public FileParameter(byte[] file) : this(file, null) { }
            public FileParameter(byte[] file, string filename) : this(file, filename, null) { }
            public FileParameter(byte[] file, string filename, string contenttype)
            {
                File = file;
                FileName = filename;
                ContentType = contenttype;
            }
        }
    }
}
