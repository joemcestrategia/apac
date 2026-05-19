using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using ApacKiosk.Database;

namespace ApacKiosk.Services
{
    public class ProgramService
    {
        private readonly DatabaseManager _db;

        public ProgramService(DatabaseManager db)
        {
            _db = db;
        }

        public List<AllowedProgram> GetForProfile(int? profileId)
        {
            var list = new List<AllowedProgram>();
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            if (profileId.HasValue)
                cmd.CommandText = "SELECT id, profile_id, name, executable_path, arguments, icon_path, is_global, created_at FROM allowed_programs WHERE profile_id = @pid OR is_global = 1 ORDER BY name";
            else
                cmd.CommandText = "SELECT id, profile_id, name, executable_path, arguments, icon_path, is_global, created_at FROM allowed_programs ORDER BY name";

            if (profileId.HasValue)
                cmd.Parameters.AddWithValue("@pid", profileId.Value);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AllowedProgram
                {
                    Id = reader.GetInt32(0),
                    ProfileId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    Name = reader.GetString(2),
                    ExecutablePath = reader.GetString(3),
                    Arguments = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IconPath = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsGlobal = reader.GetBoolean(6),
                    CreatedAt = reader.GetDateTime(7)
                });
            }
            return list;
        }

        public void Add(string name, string path, string? args, int? profileId, bool isGlobal, string? iconPath = null)
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO allowed_programs (name, executable_path, arguments, icon_path, profile_id, is_global)
                               VALUES (@n, @p, @a, @i, @pid, @g)";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@p", path);
            cmd.Parameters.AddWithValue("@a", (object?)args ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@i", (object?)iconPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pid", profileId.HasValue ? (object)profileId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@g", isGlobal ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public void Delete(int id)
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM allowed_programs WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void SeedDefaults()
        {
            using var conn = _db.GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM allowed_programs";
            var count = (long)cmd.ExecuteScalar()!;
            if (count > 0) return;

            var defaults = new (string name, string path, string? args)[]
            {
                ("Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", null),
                ("Excel", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", null),
                ("PowerPoint", @"C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE", null),
                ("Bloco de Notas", "notepad.exe", null),
                ("Calculadora", "calc.exe", null),
                ("Paint", "mspaint.exe", null),
            };

            foreach (var (name, path, args) in defaults)
            {
                if (System.IO.File.Exists(path) || path.Contains("notepad") || path.Contains("calc") || path.Contains("mspaint"))
                {
                    Add(name, path, args, null, true);
                }
            }
        }
    }

    public class AllowedProgram
    {
        public int Id { get; set; }
        public int? ProfileId { get; set; }
        public string Name { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public string? Arguments { get; set; }
        public string? IconPath { get; set; }
        public bool IsGlobal { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
