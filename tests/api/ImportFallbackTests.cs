using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitHole.Data;
using RabbitHole.Ingestion;
using Xunit;

namespace RabbitHole.Tests;

public class ImportFallbackTests
{
    private sealed class Fetcher(Func<RemoteRequest, RemoteResponse> fetch) : IRemoteFetcher
    {
        public Task<RemoteResponse> FetchAsync(RemoteRequest request, CancellationToken cancellationToken) => Task.FromResult(fetch(request));
    }

    [Theory]
    [InlineData("https://example.org/slow", "manual")]
    [InlineData("https://www.youtube.com/watch?v=abcdefghijk", "youtube")]
    [InlineData("https://soundcloud.com/test/track", "manual")]
    public async Task MetadataDeadlineStillPersistsLinkOnlyRecord(string url, string provider)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var fetcher = new Fetcher(request => request.Url.AbsolutePath == "/robots.txt"
            ? new RemoteResponse(200, request.Url, "text/plain", Encoding.UTF8.GetBytes("User-agent: *\nAllow: /"), null, null)
            : throw new OperationCanceledException("Internal HTTP deadline expired"));
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        var imported = await service.ImportUrlAsync(url, "TEST FIXTURE slow metadata", default);
        Assert.Equal(provider, imported.Provider);
        Assert.Equal("TEST FIXTURE slow metadata", imported.Title);
        Assert.Equal("Link only: The remote request timed out.", imported.ImportWarning);
        Assert.Null(imported.Body);
        Assert.Equal("metadata", imported.TextProvenance);
        Assert.Equal(imported.Id, (await db.Items.SingleAsync()).Id);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutPersistingAnImport()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        using var cancellation = new CancellationTokenSource();
        var fetcher = new Fetcher(_ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
        var service = new IngestionService(db, fetcher, new ConfigurationBuilder().Build(), NullLogger<IngestionService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportUrlAsync("https://example.org/cancelled", null, cancellation.Token));
        Assert.Empty(await db.Items.ToListAsync());
    }
}
