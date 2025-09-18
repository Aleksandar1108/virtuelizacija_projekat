using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Common
{
    public static class EisFileDiscovery
    {
        public static List<EisFileInfo> DiscoverEisFiles(string basePath)
        {
            var files = new List<EisFileInfo>();

            if (!Directory.Exists(basePath))
            {
                Console.WriteLine($"Base path does not exist: {basePath}");
                return files;
            }

            try
            {
                
                var hiokiFiles = Directory.GetFiles(basePath, "Hk_*.csv", SearchOption.AllDirectories);
                foreach (string csvFile in hiokiFiles)
                {
                    if (TryExtractSocFromHiokiFileName(csvFile, out int soc))
                    {
                        files.Add(new EisFileInfo
                        {
                            BatteryId = "B01", 
                            TestId = "Test_1", 
                            SocPercent = soc,
                            FilePath = csvFile,
                            FileName = Path.GetFileName(csvFile)
                        });
                    }
                }

                var batteryDirs = Directory.GetDirectories(basePath, "B*", SearchOption.AllDirectories)
                    .Where(dir => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(dir), @"^B\d{2}$"))
                    .ToList();

                foreach (string batteryDir in batteryDirs)
                {
                    string batteryId = Path.GetFileName(batteryDir);

                   
                    var eisDirs = Directory.GetDirectories(batteryDir, "*EIS*", SearchOption.AllDirectories);

                    foreach (string eisDir in eisDirs)
                    {
                    
                        var testDirs = Directory.GetDirectories(eisDir, "Test_*", SearchOption.TopDirectoryOnly);

                        foreach (string testDir in testDirs)
                        {
                            string testId = Path.GetFileName(testDir);

                           
                            var csvFiles = Directory.GetFiles(testDir, "*.csv", SearchOption.TopDirectoryOnly);

                            foreach (string csvFile in csvFiles)
                            {
                                if (TryExtractSocFromFileName(csvFile, out int soc))
                                {
                                    files.Add(new EisFileInfo
                                    {
                                        BatteryId = batteryId,
                                        TestId = testId,
                                        SocPercent = soc,
                                        FilePath = csvFile,
                                        FileName = Path.GetFileName(csvFile)
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error discovering EIS files: {ex.Message}");
            }

            return files.OrderBy(f => f.BatteryId).ThenBy(f => f.TestId).ThenBy(f => f.SocPercent).ToList();
        }

        private static bool TryExtractSocFromHiokiFileName(string filePath, out int soc)
        {
            soc = 0;
            string fileName = Path.GetFileNameWithoutExtension(filePath);

           
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"Hk_.*_SoC_(\d+)_");
            if (match.Success && int.TryParse(match.Groups[1].Value, out soc))
            {
             
                if (soc >= 5 && soc <= 100)
                    return true;
            }

            return false;
        }

        private static bool TryExtractSocFromFileName(string filePath, out int soc)
        {
            soc = 0;
            string fileName = Path.GetFileNameWithoutExtension(filePath);

            
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)%?");
            if (match.Success && int.TryParse(match.Groups[1].Value, out soc))
            {
               
                if (soc >= 5 && soc <= 100 && soc % 5 == 0)
                    return true;
            }

            return false;
        }

        public static int CountCsvRows(string filePath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                int totalLines = lines.Length;

         
                if (totalLines > 0 && IsHeaderLine(lines[0]))
                    return totalLines - 1;

                return totalLines;
            }
            catch
            {
                return 29; 
            }
        }

        private static bool IsHeaderLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string firstField = line.Split(',')[0].Trim().ToLowerInvariant();
            return firstField.Contains("frequency") || firstField.Contains("freq");
        }
    }
}
