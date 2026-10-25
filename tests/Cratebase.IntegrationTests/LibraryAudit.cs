using Microsoft.Data.Sqlite;

internal static class LibraryAudit
{
    public static void Run(string path)
    {
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=path,Mode=SqliteOpenMode.ReadOnly}.ToString());connection.Open();
        foreach(var sql in new[]{
            "SELECT COUNT(*) AS Tracks,SUM(club_ready) AS Club,SUM(CASE WHEN genre_raw IS NULL OR trim(genre_raw)='' THEN 1 ELSE 0 END) AS OhneGenre FROM track",
            "SELECT COALESCE(NULLIF(trim(genre_raw),''),'[ohne Genre]') AS Genre,COUNT(*) AS Tracks,SUM(club_ready) AS Club FROM track GROUP BY 1 ORDER BY COUNT(*) DESC LIMIT 90",
            "WITH RECURSIVE tree(id,path,depth) AS (SELECT id,name,0 FROM playlist_node WHERE parent_id IS NULL UNION ALL SELECT p.id,t.path||' / '||p.name,t.depth+1 FROM playlist_node p JOIN tree t ON p.parent_id=t.id) SELECT path,depth,(SELECT COUNT(*) FROM playlist_track pt WHERE pt.playlist_node_id=tree.id) AS Tracks FROM tree WHERE depth<=1 ORDER BY path",
            "SELECT relative_path,genre_raw FROM track WHERE club_ready=1 LIMIT 15"})
        {
            Console.WriteLine(sql);using var command=connection.CreateCommand();command.CommandText=sql;using var reader=command.ExecuteReader();
            while(reader.Read())Console.WriteLine(string.Join(" | ",Enumerable.Range(0,reader.FieldCount).Select(i=>reader.IsDBNull(i)?"—":reader.GetValue(i).ToString())));
        }
    }
}
