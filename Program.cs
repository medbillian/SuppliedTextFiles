using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Net.Mail;
using System.Text;
using System.Collections.Generic;

namespace SuppliedTextFileFTP
{
    class Program
    {
        static void Main(string[] args)
        {
            string logPath = ConfigurationManager.AppSettings["logPath"];
            string ftpServer = ConfigurationManager.AppSettings["ftpServer"];
            string queuePath = ConfigurationManager.AppSettings["queuePath"];
            string ftpPath = ConfigurationManager.AppSettings["ftpPath"];
            string currentDate = DateTime.Now.ToShortDateString().Replace("/", "");
            string currentTime = DateTime.Now.ToShortTimeString().Replace(":", "").Replace("AM", "").Replace("PM", "").Trim();
            string[] userNames = ConfigurationManager.AppSettings["userNames"].Split(',');
            string[] passwords = ConfigurationManager.AppSettings["passwords"].Split(',');
            string[] archivePaths = ConfigurationManager.AppSettings["ArchivePaths"].Split('|');
            int numberDays = Int32.Parse(ConfigurationManager.AppSettings["numberDays"]);
            int numberHours = Int32.Parse(ConfigurationManager.AppSettings["numberHours"]);

            using (StreamWriter streamWriter = File.CreateText(logPath + "FTPSuppliedTexts_" + currentDate + currentTime + ".txt"))
            {
                try
                {
                    FTP ftpClient = new FTP();
                    List<string> droppedFiles = new List<string>();
                    streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text - Started.");
                    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text - Started.");

                    for (int i = 0; i < userNames.Length; i++)
                    {
                        ftpClient = new FTP(ftpServer, userNames[i], passwords[i]);
                        streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text for " + userNames[i]);
                        Console.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text for " + userNames[i]);

                        // Get Contents of a Directory with Detailed File/Directory Info 
                        var detailDirectoryListing = ftpClient.directoryListSimple("").Where(x => !string.IsNullOrEmpty(x)).ToArray().OrderByDescending(x => x.ToString());

                        foreach (string file in detailDirectoryListing)
                        {
                            if ((DateTime.Now - ftpClient.getFileDateTime(file.Trim()).ToUniversalTime()) <= TimeSpan.FromDays(numberDays))
                            {
                                string fullFTPPath = Path.Combine(ftpServer, file.ToString());
                                string path = queuePath + file.ToString();

                                ftpClient.download(file.ToString(), path);
                                streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": " + fullFTPPath + " copied to " + path);
                                Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + fullFTPPath + " copied to " + path);
                            }

                            string[] zipFiles = Directory.GetFiles(queuePath, "*.zip");
                            for (int j = 0; j < zipFiles.Count(); j++)
                            {
                                ZipFile.ExtractToDirectory(zipFiles[j], queuePath);
                                Console.WriteLine(DateTime.Now.ToShortTimeString() + " Extracting " + zipFiles[j] + " to " + queuePath);
                                streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " Extracting " + zipFiles[j] + " to " + queuePath);
                            }
                        }

                        var txtFiles = Directory.GetFiles(queuePath, "*.txt", SearchOption.AllDirectories).Select(fn => new FileInfo(fn)).OrderByDescending(f => f.Name);

                        foreach (FileInfo fileInfo in txtFiles)
                        {
                            string textName = fileInfo.Name;
                            // Rename Safilo text file
                            if (fileInfo.Name.Contains("skucat"))
                            {
                                textName = "Safilo FW " + DateTime.Now.ToShortDateString().Replace("/", "") + ".txt";
                            }

                            if (fileInfo.Name.Contains("Marchon") && fileInfo.Name.Contains("Canada"))
                            {
                                object waitTimeOut = new object();
                                lock (waitTimeOut)
                                {
                                    // Pause for numberHours before processing Marchon Canada file
                                    Monitor.Wait(waitTimeOut, TimeSpan.FromMilliseconds(numberHours * 3600 * 1000));
                                }

                            }

                            File.Copy(fileInfo.FullName, Path.Combine(archivePaths[i] + textName), true);
                            File.Copy(fileInfo.FullName, Path.Combine(ftpPath + textName), true);

                            Console.WriteLine(DateTime.Now.ToShortTimeString() + " Moving " + textName + " to " + ftpPath);
                            streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " Moving " + textName + " to " + ftpPath);
                            Console.WriteLine(DateTime.Now.ToShortTimeString() + " Archiving " + textName + " to " + archivePaths[i]);
                            streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " Archiving " + textName + " to " + archivePaths[i]);

                            // Remove copied files.
                            droppedFiles.Add(textName);
                            File.Delete(fileInfo.FullName);
                        }
                        ftpClient = null;
                    }
                  
                    SendNotification(droppedFiles);
                    Console.WriteLine(DateTime.Now.ToShortTimeString() + " FTP Supplied Text - Email Notification Sent.");
                    streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " FTP Supplied Text - Email Notification Sent.");
                    Console.WriteLine(DateTime.Now.ToShortTimeString() + " FTP Supplied Text - Ended.");
                    streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " FTP Supplied Text - Ended.");
                    streamWriter.Close();
                }
                catch (WebException e)
                {
                    String status = ((FtpWebResponse)e.Response).StatusDescription;
                    streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": " + status);
                    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + status);
                }
            }
        }

        static void SendNotification(List<string> txtFiles)
        {
            string[] notificationRecipients = ConfigurationManager.AppSettings["notificationRecipients"].Split(',');
            string notificationSender = ConfigurationManager.AppSettings["notificationSender"];
            StringBuilder emailBody = new StringBuilder();
            SmtpClient smtp = new SmtpClient();

            using (MailMessage mailMessage = new MailMessage())
            {
                mailMessage.From = new MailAddress(notificationSender);
                mailMessage.Subject = "[" + System.Environment.MachineName + "]" + " Supplied Text File(s) Dropped to HF";                 

                emailBody.AppendFormat("The following Supplied Text File(s) were dropped to the hot folder: <br/>");
                emailBody.AppendFormat("<ul>");
                foreach (string fileName in txtFiles)
                {
                    emailBody.AppendFormat("<li>");
                    emailBody.AppendFormat(fileName);
                    emailBody.AppendFormat("</li>");
                }
                
                emailBody.AppendFormat("</ul><br />");
                mailMessage.Body = emailBody.ToString();
                mailMessage.IsBodyHtml = true;

                foreach (string email in notificationRecipients)
                {
                    mailMessage.To.Add(new MailAddress(email));
                }

                // Read from app.config 
                smtp.Host = ConfigurationManager.AppSettings["smtpHost"];
                smtp.EnableSsl = Convert.ToBoolean(ConfigurationManager.AppSettings["enableSsl"]);                
                smtp.Port = int.Parse(ConfigurationManager.AppSettings["smtpPort"]);
                System.Net.NetworkCredential NetworkCred = new System.Net.NetworkCredential();
                NetworkCred.UserName = ConfigurationManager.AppSettings["smtpUN"]; 
                NetworkCred.Password = ConfigurationManager.AppSettings["smtpPW"]; 

                smtp.UseDefaultCredentials = true;
                smtp.Credentials = NetworkCred;
                smtp.Send(mailMessage);
            }
        }
    }
}
