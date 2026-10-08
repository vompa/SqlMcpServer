using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SqlMcpServer.Data;

/// <summary>Erzeugt eine kleine, deterministische Shop-Datenbank zum Ausprobieren.</summary>
public static class DemoDataSeeder
{
    private const string Schema = """
        CREATE TABLE customers (
            id         INTEGER PRIMARY KEY,
            name       TEXT NOT NULL,
            email      TEXT NOT NULL UNIQUE,
            country    TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE products (
            id       INTEGER PRIMARY KEY,
            name     TEXT NOT NULL,
            category TEXT NOT NULL,
            price    REAL NOT NULL
        );
        CREATE TABLE orders (
            id          INTEGER PRIMARY KEY,
            customer_id INTEGER NOT NULL REFERENCES customers(id),
            ordered_at  TEXT NOT NULL,
            status      TEXT NOT NULL
        );
        CREATE TABLE order_items (
            order_id   INTEGER NOT NULL REFERENCES orders(id),
            product_id INTEGER NOT NULL REFERENCES products(id),
            quantity   INTEGER NOT NULL,
            unit_price REAL NOT NULL,
            PRIMARY KEY (order_id, product_id)
        );
        """;

    private static readonly (string Name, string Country)[] Customers =
    [
        ("Anna Becker", "DE"), ("Ben Fischer", "DE"), ("Clara Meier", "AT"), ("David Huber", "AT"),
        ("Elena Rossi", "IT"), ("Felix Weber", "DE"), ("Greta Keller", "CH"), ("Hans Brunner", "CH"),
    ];

    private static readonly (string Name, string Category, double Price)[] Products =
    [
        ("Mechanische Tastatur", "Zubehör", 89.90), ("USB-C-Dock", "Zubehör", 129.00),
        ("27\"-Monitor", "Displays", 279.00), ("Laptop-Ständer", "Zubehör", 39.50),
        ("Webcam 4K", "Video", 99.00), ("Headset", "Audio", 74.90),
    ];

    private static readonly string[] Statuses = ["paid", "shipped", "delivered", "cancelled"];

    public static void EnsureCreated(string path)
    {
        if (File.Exists(path))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();

        Execute(connection, tx, Schema);

        for (var i = 0; i < Customers.Length; i++)
        {
            var (name, country) = Customers[i];
            var email = name.ToLowerInvariant().Replace(' ', '.') + "@example.com";
            Execute(connection, tx,
                "INSERT INTO customers (id, name, email, country, created_at) VALUES ($id, $name, $email, $country, $created)",
                ("$id", i + 1), ("$name", name), ("$email", email), ("$country", country),
                ("$created", Iso(new DateOnly(2025, 1, 1).AddDays(i * 37))));
        }

        for (var i = 0; i < Products.Length; i++)
        {
            var (name, category, price) = Products[i];
            Execute(connection, tx,
                "INSERT INTO products (id, name, category, price) VALUES ($id, $name, $category, $price)",
                ("$id", i + 1), ("$name", name), ("$category", category), ("$price", price));
        }

        // 24 Bestellungen, rein arithmetisch erzeugt, damit die Daten reproduzierbar sind
        for (var orderId = 1; orderId <= 24; orderId++)
        {
            Execute(connection, tx,
                "INSERT INTO orders (id, customer_id, ordered_at, status) VALUES ($id, $customer, $date, $status)",
                ("$id", orderId), ("$customer", (orderId * 3 % Customers.Length) + 1),
                ("$date", Iso(new DateOnly(2026, 1, 5).AddDays(orderId * 6))),
                ("$status", Statuses[orderId % Statuses.Length]));

            var itemCount = (orderId % 3) + 1;
            for (var k = 0; k < itemCount; k++)
            {
                var productIndex = (orderId + k * 2) % Products.Length;
                Execute(connection, tx,
                    "INSERT INTO order_items (order_id, product_id, quantity, unit_price) VALUES ($o, $p, $q, $price)",
                    ("$o", orderId), ("$p", productIndex + 1), ("$q", (orderId + k) % 3 + 1),
                    ("$price", Products[productIndex].Price));
            }
        }

        tx.Commit();
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void Execute(SqliteConnection connection, SqliteTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }
}
