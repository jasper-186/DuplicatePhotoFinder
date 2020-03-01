using McMaster.Extensions.CommandLineUtils;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DuplicatePictureFinderConsole
{
    class Program
    {
        public static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        [Option(ShortName = "s1", Description = "First Folder to Compare Contents")]
        public string Source { get; }

        [Option(ShortName = "r", Description = "Recusivley search directories")]
        public bool Recurse { get; }

        private readonly string _sqlTableInsert = "Insert into FileInfo(FileName, FullPath, Length, Hash) Values(@fileName, @fullPath, @length, @hash)";

        private void OnExecute()
        {
            var resolvedSource = Path.GetFullPath(Source);
            if (!Directory.Exists(resolvedSource))
            {
                throw new DirectoryNotFoundException($"{resolvedSource} doesn't exists, please check your path.");
            }

            var fileList = Directory.EnumerateFiles(resolvedSource, "*.*", SearchOption.AllDirectories);

            string connectionString = @"Data Source=.\fileInfos.db;Cache=Shared";

            var dbLock = new Object();

            using (SqliteConnection conn = new SqliteConnection(connectionString))
            {
                conn.Open();

                // Create Table
                conn.EnsureFileInfoTable();

                // add/update the database table
                Parallel.ForEach(
                    fileList,
                    new ParallelOptions() { MaxDegreeOfParallelism = System.Environment.ProcessorCount },
                    (string filename) =>
                    {
                        var fileInfo = new FileInfo(filename);
                        byte[] filehash;
                        using (var sha256 = System.Security.Cryptography.SHA256.Create())
                        {
                            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                            {
                                filehash = sha256.ComputeHash(fs);
                            }
                        }

                        var fileHashString = BitConverter.ToString(filehash).Replace("-", "").ToLowerInvariant();

                        lock (dbLock)
                        {
                            using (SqliteCommand cmd = new SqliteCommand(_sqlTableInsert, conn))
                            {
                                cmd.Parameters.Add(new SqliteParameter("@fileName", fileInfo.Name));
                                cmd.Parameters.Add(new SqliteParameter("@fullPath", fileInfo.FullName));
                                cmd.Parameters.Add(new SqliteParameter("@length", fileInfo.Length));
                                cmd.Parameters.Add(new SqliteParameter("@hash", fileHashString));
                                cmd.ExecuteNonQuery();
                            }
                        }
                    });


                // Find Duplicates
                var SqlTableSelect = "Select Hash, Count(Hash) as FileCount FROM FileInfo Group By Hash Having FileCount > 1 Order by FileName";
                var duplicateHashes = new HashSet<string>();
                lock (dbLock)
                {
                    using (SqliteCommand cmd = new SqliteCommand(SqlTableSelect, conn))
                    {
                        var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            duplicateHashes.Add(reader["Hash"].ToString());
                        }
                    }
                }

                var filename = "duplicateFiles.txt";
                using (var fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
                using (var writer = new StreamWriter(fs, encoding: System.Text.Encoding.UTF8))
                {
                    var line = "Duplicate Files By Hash";
                    writer.WriteLine(line);
                    Console.WriteLine(line);
                    var SqlTableDuplicateSelect = "Select FileName, FullPath FROM FileInfo where Hash = @hash";
                    foreach (var hash in duplicateHashes)
                    {
                        lock (dbLock)
                        {
                            using (SqliteCommand cmd = new SqliteCommand(SqlTableDuplicateSelect, conn))
                            {
                                cmd.Parameters.Add(new SqliteParameter("@hash", hash));
                                var reader = cmd.ExecuteReader();
                                // get first line
                                reader.Read();
                                line = $"\t {reader["FileName"]}:";
                                writer.WriteLine(line);
                                Console.WriteLine(line);
                                line = $"\t\t {reader["FullPath"]}";
                                writer.WriteLine(line);
                                Console.WriteLine(line);
                                while (reader.Read())
                                {
                                    line = $"\t\t {reader["FullPath"]}";
                                    writer.WriteLine(line);
                                    Console.WriteLine(line);
                                }
                            }
                        }
                    }
                }
                conn.Close();
            }

        }
    }
}
