using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Net.Mail;
using System.Text;

namespace SuppliedTextFileFTP
{
    class Program
    {
        static void Main(string[] args)
        {
            string logPath = ConfigurationManager.AppSettings["logPath"];
            string ftpPath = ConfigurationManager.AppSettings["ftpPath"];
            string queuePath = ConfigurationManager.AppSettings["queuePath"];
            string archivePath = ConfigurationManager.AppSettings["ArchivePath"];
            string currentDate = DateTime.Now.ToShortDateString().Replace("/", "");
            string currentTime = DateTime.Now.ToShortTimeString().Replace(":", "").Replace("AM", "").Replace("PM", "").Trim();
            string[] userNames = ConfigurationManager.AppSettings["userNames"].Split(',');
            string[] passwords = ConfigurationManager.AppSettings["passwords"].Split(',');
            int numberDays = Int32.Parse(ConfigurationManager.AppSettings["numberDays"]);
            int numberHours = Int32.Parse(ConfigurationManager.AppSettings["numberHours"]);

            using (StreamWriter streamWriter = File.CreateText(logPath + "FTPSuppliedTexts_" + currentDate + currentTime + ".txt"))
            {
                try
                {
                    FTP ftpClient = new FTP();
                    streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text - Started.");
                    Console.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text - Started.");

                    for (int i = 0; i < userNames.Length; i++)
                    {
                        ftpClient = new FTP(ftpPath, userNames[i], passwords[i]);
                        streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text for " + userNames[i]);
                        Console.WriteLine(DateTime.Now.ToLongTimeString() + ": FTP Supplied Text for " + userNames[i]);

                        // Get Contents of a Directory with Detailed File/Directory Info 
                        var detailDirectoryListing = ftpClient.directoryListSimple("").Where(x => !string.IsNullOrEmpty(x)).ToArray().OrderByDescending(x => x.ToString());

                        foreach (string file in detailDirectoryListing)
                        {
                            if ((DateTime.Now - ftpClient.getFileDateTime(file.Trim()).ToUniversalTime()) <= TimeSpan.FromDays(numberDays))
                            {
                                string fullFTPPath = Path.Combine(ftpPath, file.ToString());
                                string path = queuePath + file.ToString();

                                ftpClient.download(file.ToString(), path);
                                streamWriter.WriteLine(DateTime.Now.ToLongTimeString() + ": " + fullFTPPath + " copied to " + path);
                                Console.WriteLine(DateTime.Now.ToLongTimeString() + ": " + fullFTPPath + " copied to " + path);
                            }
                        }

                        ftpClient = null;
                    }

                    string[] zipFiles = Directory.GetFiles(queuePath, "*.zip");
                    for (int i = 0; i < zipFiles.Count(); i++)
                    {
                        ZipFile.ExtractToDirectory(zipFiles[i], queuePath);
                        Console.WriteLine(DateTime.Now.ToShortTimeString() + " Extracting " + zipFiles[i] + " to " + queuePath);
                        streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " Extracting " + zipFiles[i] + " to " + queuePath);
                    }

                    // To make sure that Marchon US is processed before Marchon Canada
                    var txtFiles = Directory.GetFiles(queuePath, "*.txt", SearchOption.AllDirectories).Select(fn => new FileInfo(fn)).OrderByDescending(f => f.Name);

                    foreach (FileInfo fileInfo in txtFiles)
                    {
                        // Rename Safilo text file
                        if (fileInfo.Name.Contains("skucat"))
                        {
                            File.Copy(fileInfo.FullName, Path.Combine(archivePath + ("Safilo FW " + DateTime.Now.ToShortDateString().Replace("/", "") + ".txt")), true);
                        }
                        else if (fileInfo.Name.Contains("Marchon") && fileInfo.Name.Contains("Canada"))
                        {
                            object waitTimeOut = new object();
                            lock (waitTimeOut)
                            {
                                // Pause for numberHours before processing Marchon Canada file
                                Monitor.Wait(waitTimeOut, TimeSpan.FromMilliseconds(numberHours * 3600 * 1000));
                            }
                            File.Copy(fileInfo.FullName, Path.Combine(archivePath + fileInfo.Name), true);
                        }
                        else
                        {
                            File.Copy(fileInfo.FullName, Path.Combine(archivePath + fileInfo.Name), true);
                        }

                        Console.WriteLine(DateTime.Now.ToShortTimeString() + " Moving " + fileInfo.Name + " to " + archivePath);
                        streamWriter.WriteLine(DateTime.Now.ToShortTimeString() + " Moving " + fileInfo.Name + " to " + archivePath);
                        // Remove copied files.
                        File.Delete(fileInfo.FullName);
                    }

                    SendNotification(txtFiles);
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

        static void SendNotification(IOrderedEnumerable<FileInfo> txtFiles)
        {
            string archivePath = ConfigurationManager.AppSettings["ArchivePath"];
            string[] notificationRecipients = ConfigurationManager.AppSettings["notificationRecipients"].Split(',');
            string notificationSender = ConfigurationManager.AppSettings["notificationSender"];
            StringBuilder emailBody = new StringBuilder();
            SmtpClient smtp = new SmtpClient();

            using (MailMessage mailMessage = new MailMessage())
            {
                mailMessage.From = new MailAddress(notificationSender);
                mailMessage.Subject = "[" + System.Environment.MachineName + "]" + " Supplied Text File(s) Dropped to HF";                 

                emailBody.AppendFormat("The following Supplied Text File(s) were dropped to the hot folder.: <br/>");
                emailBody.AppendFormat("<ul>");
                foreach (FileInfo fileInfo in txtFiles)
                {
                    string textFileName = fileInfo.Name;
                    if (fileInfo.Name.Contains("skucat"))
                    {
                        textFileName = "Safilo FW " + DateTime.Now.ToShortDateString().Replace("/", "") + ".txt";
                    }
                    emailBody.AppendFormat("<li>");
                    emailBody.AppendFormat(textFileName);
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
