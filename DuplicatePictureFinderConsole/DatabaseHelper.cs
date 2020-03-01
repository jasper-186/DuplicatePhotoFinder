using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text;

namespace DuplicatePictureFinderConsole
{
    public static class DatabaseHelper
    {
        public static void EnsureFileInfoTable(this SqliteConnection conn)
        {
            if (conn.State != System.Data.ConnectionState.Open)
            {
                throw new InvalidOperationException("Connection Must be open to run this Extension");
            }

            var SqlTable = @"
                    DROP TABLE IF EXISTS FileInfo;
                    create table FileInfo(
                        FileInfoId INTEGER Primary Key,
                        FileName TEXT,
                        FullPath TEXT,
                        Length INTEGER,
                        Hash TEXT
                    );
            ";

            using (SqliteCommand cmd = new SqliteCommand(SqlTable, conn))
            {
                 cmd.ExecuteNonQuery();                
            }
        }
    }
}
