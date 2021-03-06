﻿using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace KuduExec
{
    class Program
    {
        private static void Main(string[] args)
        {
            // Disable verification for cases when we're using test stamps with test certs
            ServicePointManager.ServerCertificateValidationCallback =
                (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true;

            if (args.Length < 1)
            {
                Console.WriteLine("Usage: {0} [kudu service url (with username)]", typeof(Program).Assembly.GetName().Name);
                return;
            }

            try
            {
                Run(args[0]);
            }
            catch (Exception exception)
            {
                // Get the innermost exception
                while (exception.InnerException != null)
                {
                    exception = exception.InnerException;
                }

                Console.WriteLine(exception.Message);
            }
        }

        private static void Run(string kuduServiceUriString)
        {
            string userName = (new Uri(kuduServiceUriString)).UserInfo;

            if (!kuduServiceUriString.EndsWith("/"))
            {
                kuduServiceUriString = kuduServiceUriString + "/";
            }

            kuduServiceUriString = kuduServiceUriString + "command";

            var handler = new HttpClientHandler();
            if (!String.IsNullOrEmpty(userName))
            {
                string password;
                string[] parts = userName.Split(':');
                if (parts.Length > 1)
                {
                    userName = parts[0];
                    // support empty password
                    password = parts[1];
                }
                else
                {
                    Console.Write("Enter password: ");
                    password = ReadPassword();
                }
                handler.Credentials = new NetworkCredential(userName, password);
            }

            var client = new HttpClient(handler);
            string currentFolder = "";

            bool first = true;
            string command = "cd";

            for (; ; )
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    Console.Write("[Kudu] {0}> ", currentFolder);
                    command = Console.ReadLine();
                    if (command == null) break;

                    command = command.Trim();

                    // Ignore empty lines
                    if (String.IsNullOrEmpty(command)) continue;

                    // Add a 'cd' command at the end so we can get the working folder on the way out
                    command = command + " & cd";
                }

                var payload = new JObject(new JProperty("command", command), new JProperty("dir", currentFolder));
                HttpResponseMessage responseMessage = client.PostAsJsonAsync<JObject>(kuduServiceUriString, payload).Result.EnsureSuccessStatusCode();

                JObject result = responseMessage.Content.ReadAsAsync<JObject>().Result;
                string output = result.Value<string>("Output");
                string error = result.Value<string>("Error");
                int exitCode = result.Value<int>("ExitCode");

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine(error);
                    continue;
                }

                output = output.TrimEnd();

                // The last line should be the working folder (from the 'cd' command) so parse it out
                int lastLineIndex = output.LastIndexOf("\r\n");

                if (lastLineIndex < 0)
                {
                    currentFolder = output;
                }
                else
                {
                    currentFolder = output.Substring(lastLineIndex);

                    Console.WriteLine(output.Substring(0, lastLineIndex));
                    Console.WriteLine();
                }

                currentFolder = currentFolder.Trim();
            }
        }

        // From http://stackoverflow.com/questions/3404421/password-masking-console-application
        static string ReadPassword()
        {
            string pass = "";
            ConsoleKeyInfo key;

            do
            {
                key = Console.ReadKey(true);

                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    pass += key.KeyChar;
                    Console.Write("*");
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
                    {
                        pass = pass.Substring(0, (pass.Length - 1));
                        Console.Write("\b \b");
                    }
                }
            }
            // Stops Receving Keys Once Enter is Pressed
            while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();

            return pass;
        }
    }
}
