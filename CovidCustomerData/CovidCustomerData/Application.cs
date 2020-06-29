using Micros.Ops.Extensibility;
using Micros.PosCore.Extensibility;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Ionic.Zip;
using System.Security;
using System.Timers;

namespace CovidCustomerData
{
    public class Application : OpsExtensibilityApplication
    {
        private string StoragePath;
        private SecureString EncryptionKey;
        private Timer ConfigurationUpdateTimer;
        private string SaveFilePrefix = "DailyCustomerData";

        #region ctor

        public Application(IExecutionContext context) : base(context)
        {
            StoragePath = GetStoragePath();
            EncryptionKey = GetConfiguredEncryptionKey();
            ConfigurationUpdateTimer = new Timer();
            ConfigurationUpdateTimer.Elapsed += ConfigurationUpdateTimer_Elapsed;
            ConfigurationUpdateTimer.Interval = 30 * 1000;
            ConfigurationUpdateTimer.Start();
        }

        #endregion

        #region Extension Methods

        [ExtensibilityMethod]
        public void SaveCustomerData(object args)
        {
            SaveCustomerData();
        }

        [ExtensibilityMethod]
        public void SaveCustomerData()
        {
            if(EncryptionKey == null || EncryptionKey.Length <= 0)
            {
                string Error = "The Covid Customer Data Encryption Key is not configured.";
                OpsContext.ShowError(Error);
                base.Logger.LogAlways(Error);
                return;
            }

            string FullName = GetCustomerName();
            string PhoneNumber;

            if(!string.IsNullOrEmpty(FullName))
            {
                PhoneNumber = GetCustomerPhoneNumber();

                if (!string.IsNullOrEmpty(PhoneNumber))
                {
                    try
                    {
                        var Success = SaveDataToFile($"{FullName},{PhoneNumber},{DateTime.Now.ToString("yyyyMMddHHmmss")}");

                        if(!Success)
                        {
                            OpsContext.ShowError("Failure saving customer records. Please check EGateway log file for further information. ");
                        }
                        else
                        {
                            OpsContext.ShowMessage("Successfully saved customer records");
                            // Potentially print chit. 
                        }
                    }
                    catch (Exception ex)
                    {
                        string Error = $"CovidCustomerData: Failed to save the customer data. {System.Environment.NewLine}Reason: {ex.Message}";
                        base.Logger.LogAlways(Error);
                        OpsContext.ShowError(Error);
                    }
                }                
            }

            PurgeOldCustomerRecords();
        }

        [ExtensibilityMethod]
        public void ExportRecordsToCSV(object args)
        {
            ExportRecordsToCSV();
        }

        [ExtensibilityMethod]
        public void ExportRecordsToCSV()
        {
            int SuccessCount = 0;
            int FailureCount = 0;
            StringBuilder AllCustomerRecords = new StringBuilder();

            string RequestedEncryptionKey = OpsContext.RequestAlphaEntry("Enter Encryption Key", "Enter Encryption Key");

            if(RequestedEncryptionKey == GetPassword()) // TODO: Fix the comparison to be safer. 
            {
                bool Continue = OpsContext.AskQuestion("This will export all customer records to a CSV file on the disk. Are you sure?");

                if (!Continue)
                    return;
            }
            else
            {
                OpsContext.ShowError("Incorrect Encryption Key");
                return;
            }

            if(Directory.Exists(StoragePath))
            {
                foreach(var zipFile in Directory.GetFiles(StoragePath, $"{SaveFilePrefix}*.zip"))
                {
                    using (var dailyZipFile = ZipFile.Read(zipFile))
                    {
                        dailyZipFile.Encryption = EncryptionAlgorithm.WinZipAes256;
                        dailyZipFile.Password = GetPassword();
                        
                        foreach(var entry in dailyZipFile.Entries)
                        {
                            try
                            {
                                using (StreamReader sr = new StreamReader(entry.OpenReader()))
                                {
                                    string CustomerRecord = sr.ReadToEnd();
                                    AllCustomerRecords.AppendLine(CustomerRecord);

                                    SuccessCount++;
                                }
                            }
                            catch (Exception ex)
                            {
                                base.Logger.LogAlways($"CovidCustomerData: Failure reading from file {dailyZipFile.Name} and entry name {entry.FileName} {System.Environment.NewLine} Reason: {ex.Message}");
                                FailureCount++;
                            }
                        }
                    }                    
                }
            }
            else
            {
                OpsContext.ShowError($"Storage Path '{StoragePath}' cannot be found.");
            }

            try
            {
                string FilePath = WriteTextToDisk(AllCustomerRecords);

                OpsContext.ShowMessage($"Exported {SuccessCount} Customer Records to: '{FilePath}'{System.Environment.NewLine}Failed to export {FailureCount} Customer Records");
            }
            catch (Exception ex)
            {
                OpsContext.ShowError($"Failed to export to disk. Reason {ex.ToString()}");
            }
        }

        #endregion

        #region Private Methods

        private void ConfigurationUpdateTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            EncryptionKey = GetConfiguredEncryptionKey();
        }


        private string GetStoragePath()
        {
            string FullPath = System.Reflection.Assembly.GetAssembly(typeof(Application)).Location;
            string TheDirectory = Path.GetDirectoryName(FullPath);

            return Path.Combine(TheDirectory, "CovidCustomerDataFiles");
        }

        private string GetCustomerName()
        {
            return OpsContext.RequestAlphaEntry("Enter Customers Full Name", "Enter Customers Full Name");
        }

        private string GetCustomerPhoneNumber()
        {
            return OpsContext.RequestAlphaEntry("Enter Customers Full Phone Number", "Enter Customers Full Phone Number");
        }

        private bool SaveDataToFile(string data)
        {
            if(!Directory.Exists(StoragePath))
            {
                Directory.CreateDirectory(StoragePath);
            }

            string ZipFileName = $"{SaveFilePrefix}{DateTime.Now.ToString("yyyyMMdd")}.zip";

            string ZipPath = Path.Combine(StoragePath, ZipFileName);
            using (var DailyFile = GetZipFile(ZipPath))
            {
                string CustomerFileName = $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.txt";
                DailyFile.AddEntry(CustomerFileName, data);
                DailyFile.Save();

                return true;
            }
        }

        /// <summary>
        /// Gets a zip file if it exists or creates a new one. 
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        /// <exception cref="">Exception</exception>
        private ZipFile GetZipFile(string path)
        {
            ZipFile ZippedFile;

            try
            {
                if (File.Exists(path))
                {
                    ZippedFile = ZipFile.Read(path);
                    ZippedFile.Encryption = EncryptionAlgorithm.WinZipAes256;
                    ZippedFile.Password = GetPassword();
                }
                else
                {
                    ZippedFile = new ZipFile(path)
                    {
                        Encryption = EncryptionAlgorithm.WinZipAes256,
                        Password = GetPassword()
                    };
                    ZippedFile.Save();
                }
            }
            catch
            {
                throw;
            }

            return ZippedFile;
        }

        private void PurgeOldCustomerRecords()
        {
            try
            {
                foreach (var file in Directory.GetFiles(StoragePath))
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.CreationTime < DateTime.Now.AddDays(-21))
                        fi.Delete();
                }
            }
            catch (Exception ex)
            {
                base.Logger.LogAlways($"CovidCustomerData: Failed to purge old customer records from '{StoragePath}'. If this continues you should manually purge the files from the workstations. Reason: {ex.Message}");
            }
        }

        private SecureString GetConfiguredEncryptionKey()
        {
            SecureString SecurePassword = new SecureString();
            string PlainEncryptionKey = DataStore.ReadExtensionDataValue("PROPERTY", "EncryptionKey", OpsContext.PropertyID);
            if(!string.IsNullOrEmpty(PlainEncryptionKey))
            {
                Array.ForEach(PlainEncryptionKey.ToCharArray(), SecurePassword.AppendChar);
            }
            
            return SecurePassword;
        }

        /// <summary>
        /// To Do: Improve password storage and comparison so we dont store in plain text as much as possible
        /// </summary>
        /// <returns></returns>
        private string GetPassword()
        {
            return new System.Net.NetworkCredential(string.Empty, EncryptionKey).Password;
        }

        private string WriteTextToDisk(StringBuilder csv)
        {
            try
            {
                string ExportDirectory = Path.Combine(StoragePath, "Exports");
                string FilePath = Path.Combine(ExportDirectory, $"{DateTime.Now.ToString("yyyyMMddHHmmss")}.csv");

                if (!Directory.Exists(ExportDirectory))
                {
                    Directory.CreateDirectory(ExportDirectory);
                }

                File.WriteAllText(FilePath, csv.ToString());

                return FilePath;
            }
            catch
            {
                throw;
            }
        }

        #endregion
    }
}
