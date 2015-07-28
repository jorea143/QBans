﻿using Rocket.Core.Logging;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace QBan
{
    public class DataStore
    {
        private static string QBansBaseDir = "Plugins/QBans";
        private static string QBansBansFile = "Plugins/QBans/BansData.txt";
        private static string QBansBansbackupFile = "Plugins/QBans/BansData_bk.txt";
        private static string QBansBansExpiredExportFile = "Plugins/QBans/BansData_Expired.txt";
        private static string QBansBansFileHeader = "## Data file for the queued bans, format: target_sid><target_charname><target_steamname><admin_sid><admin_charname><admin_steamname><reason><duration><set_time";

        private static Dictionary<CSteamID, BanDataValues> QBanData = new Dictionary<CSteamID, BanDataValues>();

        public DataStore()
        {
            Initialize();
        }

        // Initialize/load the ban data here.
        private static void Initialize()
        {
            //create an empty file for the bans.
            if (!File.Exists(QBansBansFile))
            {
                SaveToFile();
            }

            string[] lines = File.ReadAllLines(@QBansBansFile);
            int i = 1;
            foreach (string value in lines)
            {
                if (value != "" && !value.StartsWith("##"))
                {
                    i++;
                    //use new style string splitting delimiter or old one based on what it matches.
                    String[] componentsFromSerial;
                    if (value.Contains("><"))
                    {
                        componentsFromSerial = value.Split(new String[] { "><" }, StringSplitOptions.None);
                    }
                    else
                    {
                        componentsFromSerial = value.Split(new char[] { '/' }, StringSplitOptions.None);
                    }

                    if (componentsFromSerial.Length == 9)
                    {
                        try
                        {
                            uint banDuration;
                            long banTime;
                            uint.TryParse(componentsFromSerial[7], out banDuration);
                            long.TryParse(componentsFromSerial[8], out banTime);

                            BanDataValues BanDataValue = new BanDataValues();
                            BanDataValue.targetSID = componentsFromSerial[0].StringToCSteamID();
                            BanDataValue.targetCharName = componentsFromSerial[1];
                            BanDataValue.targetSteamName = componentsFromSerial[2];

                            BanDataValue.adminSID = componentsFromSerial[3].StringToCSteamID();
                            BanDataValue.adminCharName = componentsFromSerial[4];
                            BanDataValue.adminSteamName = componentsFromSerial[5];

                            BanDataValue.reason = componentsFromSerial[6];
                            BanDataValue.duration = banDuration;
                            BanDataValue.setTime = DateTime.FromBinary(banTime);

                            QBanData.Add(componentsFromSerial[0].StringToCSteamID(), BanDataValue);
                        }
                        catch
                        {
                            Logger.LogWarning(String.Format("Error in parsing ban record entry, line: {0} of {1}.", i, lines.Count()));
                        }
                    }
                    else
                    {
                        Logger.LogWarning(String.Format("Failed to load an entry out of the bans data file, wrong number of values, number of values returned {0} of 9.", componentsFromSerial.Length));
                    }
                }
            }
        }

        public void Unload()
        {
            QBanData.Clear();
        }

        // Set ban data and save out to file.
        public bool SetQBanData(CSteamID key, BanDataValues data)
        {
            try
            {
                if (QBanData.ContainsKey(key))
                {
                    QBanData.Remove(key);
                }

                QBanData.Add(key, data);
                SaveToFile();
                return true;
            }
            catch
            {
                Logger.LogWarning("Error, Unable to set ban data, wrong number of array elements.");
                return false;
            }
        }

        // Remove ban data and save out to file.
        public bool RemoveQBanData(CSteamID key)
        {
            if (QBanData.ContainsKey(key))
            {
                QBanData.Remove(key);
                SaveToFile();
                return true;
            }
            return false;
        }

        // Search by playername.
        public BanDataValues GetQBanData(string playername)
        {
            try
            {
                return QBanData.Values.First(contents => contents.targetCharName.ToLower().Contains(playername.ToLower()) || contents.targetSteamName.ToLower().Contains(playername.ToLower()));
            }
            catch
            {
                return new BanDataValues();
            }
        }

        // Get exact match by CSteamID.
        public BanDataValues GetQBanData(Steamworks.CSteamID cSteamID)
        {
            BanDataValues result;
            if (QBanData.TryGetValue(cSteamID, out result))
            {
                return result;
            }
            else
            {
                return new BanDataValues();
            }
        }

        // Grab a list of bans for the bans command.
        public KeyValuePair<int, List<BanDataValues>> GetQBanDataList(string searchString, int count, int pagination)
        {
            // Grab a list of matches of the searchString out of the QBanData dictionary.
            List<BanDataValues> matches = new List<BanDataValues>();
            if (searchString == String.Empty)
            {
                matches = QBanData.Values.OrderBy(o => o.setTime).ToList();
            }
            else
            {
                matches = QBanData.Values.Where(contents => contents.targetCharName.ToLower().Contains(searchString.ToLower()) || contents.targetSteamName.ToLower().Contains(searchString.ToLower())).OrderBy(o => o.setTime).ToList();
            }
            int matchCount = matches.Count;
            int index;
            int numbeOfRecords;

            // Do the math for the pagenation so that no negative numbers are entered into matches.GetRange.
            if (matchCount - (count * pagination) <= 0)
            {
                index = 0;
                numbeOfRecords = matchCount - count * (pagination - 1);
                if (numbeOfRecords < 0)
                {
                    numbeOfRecords = 0;
                }
            }
            else
            {
                index = matchCount - count * pagination;
                numbeOfRecords = count;
            }
            // Return index posistion and the list.
            return new KeyValuePair<int, List<BanDataValues>>(index + 1, new List<BanDataValues>(matches.GetRange(index, numbeOfRecords)));
        }

        // Check for expired bans in the ban data, remove expired.
        public void CheckExpiredBanData()
        {
            List<CSteamID> expiredList = new List<CSteamID>();
            foreach (KeyValuePair<CSteamID, BanDataValues> pair in QBanData)
            {
                if ((pair.Value.duration - (DateTime.Now - pair.Value.setTime).TotalSeconds) <= 0)
                {
                    expiredList.Add(pair.Key);
                    SteamBlacklist.unban(pair.Key);
                    SteamBlacklist.save();
                }
            }
            if (QBan.Instance.Configuration.Instance.EnableExpiredExport && expiredList.Count != 0)
            {
                StreamWriter file = new StreamWriter(QBansBansExpiredExportFile, true);
                foreach (CSteamID cSteamID in expiredList)
                {
                    BanDataValues data;
                    QBanData.TryGetValue(cSteamID, out data);
                    WriteLine(file, data);
                }
                file.Close();
            }
            foreach (CSteamID key in expiredList)
            {
                QBanData.Remove(key);
            }
            if (expiredList.Count != 0)
            {
                SaveToFile();
            }
        }

        // Save to file.
        private static void SaveToFile()
        {
            //Create the folder where the data file is to be stored
            Directory.CreateDirectory(QBansBaseDir);
            //create a backup of the main data file before writing to it.
            if (File.Exists(QBansBansbackupFile))
            {
                File.Delete(QBansBansbackupFile);
            }
            if (File.Exists(QBansBansFile))
            {
                File.Copy(QBansBansFile, QBansBansbackupFile);
            }
            // Iterate through the dictionary and parse the entries out to file.
            StreamWriter file = new StreamWriter(QBansBansFile, false);
            file.WriteLine(QBansBansFileHeader);
            foreach (KeyValuePair<CSteamID, BanDataValues> pair in QBanData)
            {
                WriteLine(file, pair.Value);
            }
            file.Close();
        }

        private static void WriteLine(StreamWriter file, BanDataValues data)
        {
            try
            {
                file.WriteLine(data.targetSID.ToString() + "><" + data.targetCharName + "><" + data.targetSteamName + "><" + data.adminSID.ToString() + "><" + data.adminCharName + "><" + data.adminSteamName + "><" + data.reason + "><" + data.duration.ToString() + "><" + data.setTime.ToBinary().ToString());
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }
    }
}
