using SqlMcpServer.Data;

namespace SqlMcpServer.Tests.Data;

public class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT * FROM customers")]
    [InlineData("select id, name from customers where country = 'DE';")]
    [InlineData("WITH t AS (SELECT 1 AS x) SELECT x FROM t")]
    [InlineData("SELECT 'drop table x' AS text")]              // Schlüsselwort nur im String-Literal
    [InlineData("SELECT \"update\" FROM weird_table")]           // quotierter Bezeichner
    [InlineData("SELECT c.name, COUNT(*) FROM customers c JOIN orders o ON o.customer_id = c.id GROUP BY c.name")]
    [InlineData("SELECT replace(name, 'a', 'b') FROM customers")] // REPLACE als Funktion ist harmlos
    public void Allows_read_only_queries(string sql) =>
        SqlGuard.EnsureReadOnly(sql);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_empty_queries(string? sql) =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly(sql));

    [Theory]
    [InlineData("delete from customers")]
    [InlineData("drop table customers")]
    [InlineData("insert into customers(name) values ('x')")]
    [InlineData("update customers set name = 'x'")]
    [InlineData("pragma writable_schema = 1")]
    [InlineData("attach database 'other.db' as o")]
    [InlineData("with t as (select 1) delete from customers")]   // schreibend hinter WITH
    [InlineData("select load_extension('evil')")]
    public void Rejects_write_and_admin_statements(string sql) =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly(sql));

    [Theory]
    [InlineData("select 1; drop table customers")]
    [InlineData("select 1; select 2")]
    public void Rejects_multiple_statements(string sql) =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly(sql));

    [Theory]
    [InlineData("select 1 -- harmlos")]
    [InlineData("select /* x */ 1")]
    public void Rejects_comments(string sql) =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly(sql));

    [Fact]
    public void Rejects_unterminated_literal() =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly("select 'abc"));

    [Fact]
    public void Rejects_overlong_query() =>
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly("select " + new string('1', SqlGuard.MaxLength)));

    [Fact]
    public void Escaped_quote_does_not_hide_following_statement() =>
        // 'x''; y' ist ein einziges Literal, danach folgt aber eine echte zweite Anweisung
        Assert.Throws<QueryRejectedException>(() => SqlGuard.EnsureReadOnly("select 'x''; y' ; drop table y"));
}
