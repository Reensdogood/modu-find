using Microsoft.Data.Sqlite;

namespace DocumentFinder.Core;

public sealed class SearchStore
{
    public string DatabasePath { get; }
    private readonly string connectionString;

    public SearchStore(string path)
    {
        DatabasePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            CREATE TABLE IF NOT EXISTS documents(
              id INTEGER PRIMARY KEY, path TEXT NOT NULL UNIQUE COLLATE NOCASE, name TEXT NOT NULL,
              extension TEXT NOT NULL, kind TEXT NOT NULL, size INTEGER NOT NULL, modified INTEGER NOT NULL,
              root TEXT NOT NULL, sheets TEXT NOT NULL DEFAULT '', body TEXT NOT NULL DEFAULT '',
              status TEXT NOT NULL DEFAULT '파일명만', note TEXT NOT NULL DEFAULT '',
              name_n TEXT NOT NULL, path_n TEXT NOT NULL, sheets_n TEXT NOT NULL DEFAULT '',
              search_text TEXT NOT NULL, indexed INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_documents_kind ON documents(kind);
            CREATE INDEX IF NOT EXISTS ix_documents_modified ON documents(modified);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(search_text, content='documents', content_rowid='id', tokenize='trigram');
            CREATE TRIGGER IF NOT EXISTS docs_ai AFTER INSERT ON documents BEGIN
              INSERT INTO documents_fts(rowid,search_text) VALUES(new.id,new.search_text); END;
            CREATE TRIGGER IF NOT EXISTS docs_ad AFTER DELETE ON documents BEGIN
              INSERT INTO documents_fts(documents_fts,rowid,search_text) VALUES('delete',old.id,old.search_text); END;
            CREATE TRIGGER IF NOT EXISTS docs_au AFTER UPDATE OF search_text ON documents BEGIN
              INSERT INTO documents_fts(documents_fts,rowid,search_text) VALUES('delete',old.id,old.search_text);
              INSERT INTO documents_fts(rowid,search_text) VALUES(new.id,new.search_text); END;
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA busy_timeout=10000"; cmd.ExecuteNonQuery();
        return c;
    }

    public bool NeedsUpdate(FileRecord file)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT size,modified,status FROM documents WHERE path=$p"; cmd.Parameters.AddWithValue("$p", file.Path);
        using var r = cmd.ExecuteReader();
        return !r.Read() || r.GetInt64(0) != file.Size || r.GetInt64(1) != file.ModifiedTicks
            || r.GetString(2) is "파일명만" or "온라인 전용" or "읽기 실패" or "시간 초과";
    }

    public void RegisterFiles(IEnumerable<FileRecord> files)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO documents(path,name,extension,kind,size,modified,root,name_n,path_n,search_text)
            VALUES($p,$n,$e,$k,$s,$m,$r,$nn,$pn,$text)
            ON CONFLICT(path) DO UPDATE SET root=excluded.root;
            """;
        foreach (var f in files)
        {
            cmd.Parameters.Clear(); var name = Path.GetFileName(f.Path); var ext = Path.GetExtension(f.Path).ToLowerInvariant();
            cmd.Parameters.AddWithValue("$p", f.Path); cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$e", ext); cmd.Parameters.AddWithValue("$k", TextRules.Kind(ext));
            cmd.Parameters.AddWithValue("$s", f.Size); cmd.Parameters.AddWithValue("$m", f.ModifiedTicks);
            cmd.Parameters.AddWithValue("$r", f.Root); cmd.Parameters.AddWithValue("$nn", TextRules.Normalize(name));
            cmd.Parameters.AddWithValue("$pn", TextRules.Normalize(f.Path)); cmd.Parameters.AddWithValue("$text", TextRules.Normalize(f.Path));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Update(FileRecord f, ExtractedDocument d)
    {
        RegisterFiles([f]);
        using var c = Open(); using var cmd = c.CreateCommand();
        var sheets = string.Join("\n", d.Sheets);
        cmd.CommandText = """
          UPDATE documents SET size=$s, modified=$m, sheets=$sh, body=$b, status=$st, note=$note,
          sheets_n=$sn, search_text=$text, indexed=$at WHERE path=$p
          """;
        cmd.Parameters.AddWithValue("$p", f.Path); cmd.Parameters.AddWithValue("$s", f.Size); cmd.Parameters.AddWithValue("$m", f.ModifiedTicks);
        cmd.Parameters.AddWithValue("$sh", sheets); cmd.Parameters.AddWithValue("$b", d.Content);
        cmd.Parameters.AddWithValue("$st", d.Status); cmd.Parameters.AddWithValue("$note", d.Note);
        cmd.Parameters.AddWithValue("$sn", TextRules.Normalize(sheets));
        cmd.Parameters.AddWithValue("$text", TextRules.Normalize(f.Path + "\n" + sheets + "\n" + d.Content));
        cmd.Parameters.AddWithValue("$at", DateTime.UtcNow.Ticks); cmd.ExecuteNonQuery();
    }

    public List<SearchHit> Search(SearchOptions options, out int total)
    {
        var terms = TextRules.Terms(options.Query);
        using var c = Open(); using var cmd = c.CreateCommand();
        var where = new List<string>(); var scores = new List<string>();
        if (options.Kind != "전체") { where.Add("kind=$kind"); cmd.Parameters.AddWithValue("$kind", options.Kind); }
        var longTerms = terms.Where(t => t.EnumerateRunes().Count() >= 3).ToArray();
        if (longTerms.Length > 0 && !options.NamesOnly)
        {
            where.Add("id IN (SELECT rowid FROM documents_fts WHERE documents_fts MATCH $fts)");
            cmd.Parameters.AddWithValue("$fts", string.Join(" AND ", longTerms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"")));
        }
        for (var i = 0; i < terms.Length; i++)
        {
            var p = "$q" + i; cmd.Parameters.AddWithValue(p, terms[i]);
            where.Add(options.NamesOnly ? $"(instr(name_n,{p})>0 OR instr(path_n,{p})>0)" : $"instr(search_text,{p})>0");
            scores.Add($"(CASE WHEN instr(name_n,{p})>0 THEN 12 ELSE 0 END + CASE WHEN instr(sheets_n,{p})>0 THEN 8 ELSE 0 END)");
        }
        var filter = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";
        var score = scores.Count > 0 ? string.Join("+", scores) : "0";
        cmd.CommandText = "SELECT COUNT(*) FROM documents" + filter; total = Convert.ToInt32(cmd.ExecuteScalar());
        cmd.CommandText = $"SELECT id,name,path,extension,size,modified,sheets,body,status,note,({score}) AS rank FROM documents" + filter
            + (options.NewestFirst ? " ORDER BY modified DESC,id DESC" : " ORDER BY rank DESC,modified DESC,id DESC") + " LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(options.Limit, 1, 2000));
        using var r = cmd.ExecuteReader(); var result = new List<SearchHit>();
        while (r.Read()) result.Add(new SearchHit(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4),r.GetInt64(5),
            r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetInt32(10)));
        return result;
    }

    public IndexStats Stats()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*),COALESCE(SUM(status='완료' OR status='일부 내용'),0),COALESCE(SUM(status='파일명만'),0),COALESCE(SUM(status NOT IN ('완료','일부 내용','파일명만')),0) FROM documents";
        using var r=cmd.ExecuteReader();r.Read();
        return new IndexStats(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3),new FileInfo(DatabasePath).Length);
    }

    public void SetSetting(string key,string value)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k",key);cmd.Parameters.AddWithValue("$v",value);cmd.ExecuteNonQuery();
    }
    public string? GetSetting(string key)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM settings WHERE key=$k";cmd.Parameters.AddWithValue("$k",key);
        return cmd.ExecuteScalar() as string;
    }

    public int RemoveMissing(IEnumerable<string> roots, CancellationToken token)
    {
        var reduced=TextRules.ReduceRoots(roots); var missing=new List<long>();
        using(var c=Open())using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="SELECT id,path FROM documents";using var r=cmd.ExecuteReader();
            while(r.Read())
            {
                token.ThrowIfCancellationRequested();var path=r.GetString(1);
                // An unavailable drive or root must never erase its index.
                if (!reduced.Any(root=>Directory.Exists(root) && TextRules.IsWithin(path,root)))continue;
                try { _=File.GetAttributes(path); } catch(FileNotFoundException){missing.Add(r.GetInt64(0));}
                catch(DirectoryNotFoundException){missing.Add(r.GetInt64(0));} catch(IOException){} catch(UnauthorizedAccessException){}
            }
        }
        using var cc=Open();using var tx=cc.BeginTransaction();using var del=cc.CreateCommand();del.Transaction=tx;del.CommandText="DELETE FROM documents WHERE id=$id";
        foreach(var id in missing){token.ThrowIfCancellationRequested();del.Parameters.Clear();del.Parameters.AddWithValue("$id",id);del.ExecuteNonQuery();}tx.Commit();return missing.Count;
    }
}
