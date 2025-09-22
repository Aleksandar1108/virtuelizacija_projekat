using System;
using System.Globalization;
using System.IO;

namespace Common
{
    public class SimpleEisCsvReader : IDisposable
    {
        private readonly string csvFilePath;
        private readonly string rejectsFilePath;
        private StreamReader reader;
        private StreamWriter rejectsWriter;
        private int currentRowIndex = 0;
        private int acceptedCount = 0;
        private int rejectedCount = 0;
        private bool disposed = false;

        public int AcceptedCount => acceptedCount;
        public int RejectedCount => rejectedCount;

        public SimpleEisCsvReader(string csvFilePath, string rejectsFilePath)
        {
            this.csvFilePath = csvFilePath;
            this.rejectsFilePath = rejectsFilePath;

            if (!File.Exists(csvFilePath))
                throw new FileNotFoundException($"CSV file not found: {csvFilePath}");

            reader = new StreamReader(csvFilePath);

         
            Directory.CreateDirectory(Path.GetDirectoryName(rejectsFilePath));
            rejectsWriter = new StreamWriter(rejectsFilePath, false) { AutoFlush = true };
            rejectsWriter.WriteLine("RowIndex,Reason,RawLine");

            
            if (!reader.EndOfStream)
            {
                string firstLine = reader.ReadLine();
             
                if (IsHeaderLine(firstLine))
                {
                    Console.WriteLine($"Skipped header: {firstLine}");
                }
                else
                {
                  
                    reader.BaseStream.Seek(0, SeekOrigin.Begin);
                    reader = new StreamReader(reader.BaseStream);
                }
            }
        }

        private bool IsHeaderLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string[] parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.None);
            if (parts.Length < 6)
                return false;

         
            string firstField = parts[0].Trim().ToLowerInvariant();
            if (firstField.Contains("frequency") || firstField.Contains("freq"))
                return true;

         
            var ci = CultureInfo.InvariantCulture;
            return !double.TryParse(parts[0].Trim(), NumberStyles.Float, ci, out _);
        }

        public bool TryReadNext(out EisSample sample)
        {
            sample = null;

            if (reader.EndOfStream)
                return false;

            string line = reader.ReadLine();
            currentRowIndex++;

            if (string.IsNullOrWhiteSpace(line))
            {
                rejectedCount++;
                rejectsWriter.WriteLine($"{currentRowIndex},Empty line,\"{line}\"");
                return TryReadNext(out sample); 
            }

            if (EisSample.TryParseCsv(line, currentRowIndex, out sample, out string error))
            {
                acceptedCount++;
                return true;
            }
            else
            {
                rejectedCount++;
                rejectsWriter.WriteLine($"{currentRowIndex},{error.Replace(',', ';')},\"{line}\"");
               
                return TryReadNext(out sample);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            try
            {
                reader?.Dispose();
                rejectsWriter?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing SimpleEisCsvReader: {ex.Message}");
            }
            finally
            {
                disposed = true;
            }
        }
    }
}