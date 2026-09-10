using Testcontainers.PostgreSql;

namespace RoomBooking.Tests.Integration;

/// <summary>
/// Starts a real PostgreSQL container for the test run.
/// The in-memory EF provider cannot be used here: it does not enforce unique
/// indexes, which is exactly the behaviour these tests are about.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
