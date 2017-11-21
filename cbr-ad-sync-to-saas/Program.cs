using System;
using System.Configuration;
using System.DirectoryServices;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using System.Net;
using System.IO;
using System.Diagnostics;

namespace cbr_ad_sync_to_saas
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();

        const string UPLOAD_URI = "/api/rest.php/imports-user?action=import";
        const string AUTH_URI = "/api/rest.php/auth/session";

        const string APP_NAME = "Collaborator AD sync";

        readonly static string[] AD_PROPS_TO_LOAD = { "samaccountname", "City", "Department", "Title", "OfficePhone", "objectguid", "givenname", "cn", "sn", "mail" };

        static void Main(string[] args)
        {
            try
            {
                bool saveLocal = false;
                bool debugAd = false;
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
                    }
                }
                Console.WriteLine("Start");
                Console.WriteLine("Read data from AD ...");

                List<string> items = new List<string>();
                List<string> item = new List<string>();
                items.Add("ID;Фамилия;Имя;Отчество;Логин;Почта;Пароль;Дата рождения;Пол (Ж-1, М-0);Город;Подразделение;Должность;Метки;телефон");

                DirectoryEntry ldapConnection = new DirectoryEntry(
                    ConfigurationManager.AppSettings["ad-server"],
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
                        item.Add(retrieveADProperty(result, "City"));//city                        
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

                        item.Add(retrieveADProperty(result, "OfficePhone"));//phone
                        items.Add(String.Join(";", item.ToArray()));
                    }
                }

                if (debugAd)
                {
                    return;
                }

                /*foreach (String itm in items)
                {
                    Console.WriteLine(itm);
                }*/

                Console.WriteLine("AD done. Found " + items.Count + " items");

                if (saveLocal)
                {
                    string outFileName = "rusers.csv";
                    Console.WriteLine("Save to file " + outFileName);
                    //String.Join("\n", items.ToArray())

                    StreamWriter file = new StreamWriter(Directory.GetCurrentDirectory() + "/" + outFileName);
                    file.WriteLine(String.Join("\n", items.ToArray()));
                    file.Close();

                    return;
                }

                Console.WriteLine("Auth on the remote server ...");
                string userData = authOnRemoteServer(ConfigurationManager.AppSettings["cbr-server"] + AUTH_URI,
                    ConfigurationManager.AppSettings["cbr-login"], ConfigurationManager.AppSettings["cbr-password"]);

                dynamic user = JsonConvert.DeserializeObject(userData);

                Console.WriteLine("Auth done");

                Console.WriteLine("Upload file to the server ...");

                //byte[] utf8bytes = Encoding.Default.GetBytes(String.Join("\n", items.ToArray()));

                Console.WriteLine(uploadFile(ConfigurationManager.AppSettings["cbr-server"] + UPLOAD_URI,
                    user.access_token.ToString(), String.Join("\n", items.ToArray())));

                Console.WriteLine("All done");

                ldapConnection.Close();

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
            } else if (searchResult.Properties[propertyName].Count > 0)
            {
                result = searchResult.Properties[propertyName][0].ToString();//retrieving properties
            }

            return result;
        }

        private static string authOnRemoteServer(string actionUrl, string login, string password)
        {
            string data = "{\"email\": \"" + login + "\", \"password\": \"" + password + "\"}";
            var content = new StringContent(data, Encoding.UTF8, "application/json");
            //var content = new FormUrlEncodedContent(values);
            var response = httpClient.PostAsync(actionUrl, content).Result;
            if (!response.IsSuccessStatusCode)
            {
                return null;//todo add exception here
            }
            return response.Content.ReadAsStringAsync().Result;
        }

        private static string uploadFile(string actionUrl, string authToken, string fileContent)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(fileContent);

            Dictionary<string, object> postParameters =
                new Dictionary<string, object>();
            postParameters.Add("auth_token", authToken);
            postParameters.Add("file",
                new FormUpload.FileParameter(bytes, "file.csv", "text/csv"));

            // Create request and receive response
            HttpWebResponse webResponse =
                FormUpload.MultipartFormDataPost(actionUrl, "sync", postParameters);

            // Process response
            StreamReader responseReader = new StreamReader(webResponse.GetResponseStream());
            string fullResponse = responseReader.ReadToEnd();
            webResponse.Close();
            //Response.Write(fullResponse);

            return fullResponse;
        }
    }

    // Implements multipart/form-data POST in C# http://www.ietf.org/rfc/rfc2388.txt
    // http://www.briangrinstead.com/blog/multipart-form-post-in-c
    public static class FormUpload
    {
        private static readonly Encoding encoding = Encoding.UTF8;
        public static HttpWebResponse MultipartFormDataPost(string postUrl, string userAgent, Dictionary<string, object> postParameters)
        {
            string formDataBoundary = String.Format("----------{0:N}", Guid.NewGuid());
            string contentType = "multipart/form-data; boundary=" + formDataBoundary;

            byte[] formData = GetMultipartFormData(postParameters, formDataBoundary);

            return PostForm(postUrl, userAgent, contentType, formData);
        }

        private static HttpWebResponse PostForm(string postUrl, string userAgent, string contentType, byte[] formData)
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
            request.CookieContainer = new CookieContainer();
            request.ContentLength = formData.Length;
            request.Timeout = 60 * 60 * 1000;

            // You could add authentication here as well if needed:
            // request.PreAuthenticate = true;
            // request.AuthenticationLevel = System.Net.Security.AuthenticationLevel.MutualAuthRequested;
            // request.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(System.Text.Encoding.Default.GetBytes("username" + ":" + "password")));

            // Send the form data to the request.
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(formData, 0, formData.Length);
                requestStream.Close();
            }

            return request.GetResponse() as HttpWebResponse;
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
                    string header = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\";\r\nContent-Type: {3}\r\n\r\n",
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
