using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Fuse.Contracts.Api;
using Fuse.Contracts.Auth;
using Fuse.Domain.Identity;
using Fuse.Infrastructure.Multitenancy;
using Fuse.Infrastructure.Persistence;
using Fuse.Shared.Authorization;
using Fuse.Shared.Multitenancy;

[assembly: AssemblyFixture(typeof(Fuse.Web.Tests.TestApp))]

namespace Fuse.Web.Tests;

/// <summary>
/// The real app against a throwaway database on the local SQL Server (never the dev database):
/// schema created from the EF model, the repo's stored procedures applied, roles seeded, and one
/// admin + one guest account. The database is dropped when the run ends.
/// </summary>
public sealed partial class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@tests.invalid";
    public const string GuestEmail = "guest@tests.invalid";
    public const string Password = "test1234";
    /// <summary>The seeded property. Tests that need "any" property use it, never one another test created (and may delete).</summary>
    public const string SeedPropertyName = "Test Hostel";

    /// <summary>The public booking API's test client (acting as the admin) and one whose user is a guest (refused).</summary>
    public const string ApiClientId = "TestClient", ApiClientSecret = "test-client-secret", GuestApiClientId = "GuestClient";

    private static readonly string Server =
        Environment.GetEnvironmentVariable("TRAVALOUD_TEST_SQL")
        ?? "Server=localhost;User Id=sa;Password=SuperHard123;TrustServerCertificate=True;Encrypt=False;";

    private readonly string _database = $"travaloud_tests_{Guid.NewGuid():N}";

    /// <summary>
    /// Where stored procedures, table types and computed columns come from: they live in the database, not in the
    /// project, so each test database copies them from the local dev database (or TRAVALOUD_TEST_SOURCE_DB).
    /// </summary>
    private static readonly string SourceDatabase = Environment.GetEnvironmentVariable("TRAVALOUD_TEST_SOURCE_DB") ?? "travaloud";

    public string ConnectionString => $"{Server}Database={_database};";

    public FakePayments Payments { get; } = new();
    public CapturedEmails Emails { get; } = new();
    public CapturedWebhooks Webhooks { get; } = new();

    /// <summary>Database objects that couldn't be copied from the dev database (should be empty; asserted by a test).</summary>
    public List<string> ProcedureFailures { get; } = [];

    /// <summary>Dev-database procedures that don't fit this model (legacy-only ones), not copied.</summary>
    public List<string> SkippedProcedures { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // The real appsettings files aren't in the repository: the committed example is the base (lowest priority), so
        // a fresh clone runs the tests too (a local appsettings.json, and everything set below, still overrides it).
        builder.ConfigureAppConfiguration((context, configuration) => configuration.Sources.Insert(0,
            new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
            {
                Path = "appsettings.Example.json",
                Optional = true,
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(context.HostingEnvironment.ContentRootPath),
            }));
        // As Development does: a service the container can't build (e.g. a validator with a non-DI
        // constructor argument) fails every test instead of only surfacing when the app starts.
        builder.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:FormsPer10Minutes", "100000");
        builder.UseSetting("RateLimits:ApiTokenPerMinute", "100000");
        builder.UseSetting("RateLimits:ApiPerMinute", "100000");
        // No Hangfire: InlineJobs runs queued jobs at once, and tests run the scheduled ones themselves.
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Site:ContactEmail", "team@tests.invalid");
        // Never production's storage: files go to the local folder.
        builder.UseSetting("AzureStorageSettings:ConnectionString", "");
        builder.UseSetting("ApiSettings:Jwt:Key", "tests-only-signing-key-0123456789abcdef");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Error");
        // Never call the real Cloudbeds API from tests.
        // Never call Cloudbeds or Stripe, send email or call partner webhooks from tests.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<Application.Common.Cloudbeds.ICloudbedsClient, FakeCloudbeds>();
            services.AddSingleton(Payments);
            services.AddSingleton<Application.Common.Payments.IPaymentGateway>(Payments);
            services.AddSingleton(Emails);
            services.AddSingleton<Application.Common.Mail.IEmailSender>(Emails);
            services.AddSingleton(Webhooks);
            services.AddSingleton<Application.Common.Payments.IPaidBookingWebhook>(Webhooks);
            services.AddSingleton<Fuse.Application.Common.Jobs.IBackgroundJobs, InlineJobs>();
            services.AddSingleton<Application.Common.Security.ICaptcha, FakeCaptcha>();
        });
    }

    public async ValueTask InitializeAsync()
    {
        _ = Server; // Fail fast with a clear message if the local SQL Server isn't reachable.
        await using (var scope = Services.CreateAsyncScope())
        {
            var tenants = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await tenants.Database.EnsureCreatedAsync();
            tenants.TenantInfo.Add(new FuseTenantInfo
            {
                Id = MultitenancyConstants.Fuse.Id,
                Identifier = MultitenancyConstants.Fuse.Identifier,
                Name = "Fuse (tests)",
                AdminEmail = "",
                IsActive = true,
                ValidUpto = DateTime.UtcNow.AddYears(10),
            });
            await tenants.SaveChangesAsync();
        }

        var tenant = new FuseTenantInfo { Id = MultitenancyConstants.Fuse.Id, Identifier = MultitenancyConstants.Fuse.Identifier };
        Services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(tenant);

        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        await CopyDatabaseObjectsAsync();
        await DatabaseMigrator.SeedAsync(Services);

        Services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext = new MultiTenantContext<FuseTenantInfo>(tenant);
        await using (var scope = Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await CreateUserAsync(users, AdminEmail, FuseRoles.Admin);
            await CreateUserAsync(users, GuestEmail, FuseRoles.Guest);

            // The public API's clients act as a user (known only now): the admin, and a guest (who isn't allowed).
            var api = Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Fuse.Web.PublicApi.ApiSettings>>().Value;
            api.Clients.Add(new() { ClientId = ApiClientId, ClientSecret = ApiClientSecret, UserId = (await users.FindByEmailAsync(AdminEmail))!.Id });
            api.Clients.Add(new() { ClientId = GuestApiClientId, ClientSecret = ApiClientSecret, UserId = (await users.FindByEmailAsync(GuestEmail))!.Id });
        }

        await using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand(SeedCatalogSql, connection);
            await seed.ExecuteNonQueryAsync();
        }
    }

    /// <summary>An HTTP client signed in as the given user (cookies kept between calls).</summary>
    public async Task<HttpClient> SignedInAsync(string email, bool staffOnly = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ApiHeaders.Marker, "1");
        var response = await client.PostAsJsonAsync(AuthRoutes.SignIn, new SignInRequest { Email = email, Password = Password, StaffOnly = staffOnly });
        response.EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>The browser's IApi implementation, signed in as an admin: exercises the real HTTP path.</summary>
    public async Task<IApi> AdminApiAsync() =>
        new Fuse.Web.Client.Api.HttpApi(await SignedInAsync(AdminEmail, staffOnly: true));

    /// <summary>A client of the public booking API, with a bearer token from /api/token.</summary>
    public async Task<HttpClient> PublicApiAsync(string clientId = ApiClientId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/token", new { clientId, clientSecret = ApiClientSecret });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public HttpClient Anonymous()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ApiHeaders.Marker, "1");
        return client;
    }

    private static async Task CreateUserAsync(UserManager<ApplicationUser> users, string email, string role)
    {
        var user = new ApplicationUser
        {
            UserName = email, Email = email, FirstName = role, LastName = "Tester", FullName = $"{role} Tester",
            IsActive = true, EmailConfirmed = true,
        };
        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
    }

    /// <summary>
    /// Copies what the EF model doesn't create from the dev database: table types (procedure parameters), computed
    /// columns, then every stored procedure (as CREATE OR ALTER).
    /// </summary>
    private async Task CopyDatabaseObjectsAsync()
    {
        List<string> statements = [];
        List<(string Name, string Definition)> procedures;
        await using (var source = new SqlConnection($"{Server}Database={SourceDatabase};"))
        {
            await source.OpenAsync();
            statements.AddRange(await TableTypesAsync(source));
            statements.AddRange(await ReadAsync(source, ComputedColumnsSql));
            procedures = await ReadPairsAsync(source, "SELECT p.name, OBJECT_DEFINITION(p.object_id) FROM sys.procedures p WHERE p.is_ms_shipped = 0 ORDER BY p.name");
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var statement in statements)
        {
            try
            {
                await using var command = new SqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                ProcedureFailures.Add($"{statement[..Math.Min(statement.Length, 120)].ReplaceLineEndings(" ")}…: {ex.Message}");
            }
        }

        // Procedures only legacy calls can reference columns this model doesn't have: skipped (a test calling one fails).
        foreach (var (name, definition) in procedures)
        {
            try
            {
                await using var command = new SqlCommand(CreateOrAlter().Replace(definition, "CREATE OR ALTER PROCEDURE", 1), connection);
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException ex)
            {
                SkippedProcedures.Add($"{name}: {ex.Message}");
            }
        }
    }

    private static async Task<List<(string, string)>> ReadPairsAsync(SqlConnection connection, string sql)
    {
        List<(string, string)> rows = [];
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (!reader.IsDBNull(1))
                rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    /// <summary>An ALTER TABLE … ADD … AS … for each computed column (skipped where the table lacks it or has it).</summary>
    private const string ComputedColumnsSql = """
        SELECT 'IF OBJECT_ID(N''' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ''') IS NOT NULL AND COL_LENGTH(N''' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ''', N''' + c.name + ''') IS NULL '
             + 'ALTER TABLE ' + QUOTENAME(s.name) + '.' + QUOTENAME(t.name) + ' ADD ' + QUOTENAME(c.name) + ' AS ' + c.definition + CASE WHEN c.is_persisted = 1 THEN ' PERSISTED' ELSE '' END
        FROM sys.computed_columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
        """;

    private static async Task<List<string>> ReadAsync(SqlConnection connection, string sql)
    {
        List<string> rows = [];
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (!reader.IsDBNull(0))
                rows.Add(reader.GetString(0));
        return rows;
    }

    /// <summary>CREATE TYPE … AS TABLE for each user table type, from its columns.</summary>
    private static async Task<List<string>> TableTypesAsync(SqlConnection connection)
    {
        const string Sql = """
            SELECT QUOTENAME(s.name) + '.' + QUOTENAME(tt.name), c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable
            FROM sys.table_types tt
            JOIN sys.schemas s ON s.schema_id = tt.schema_id
            JOIN sys.columns c ON c.object_id = tt.type_table_object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            ORDER BY tt.name, c.column_id
            """;
        var columns = new List<(string Type, string Column, string SqlType, short MaxLength, byte Precision, byte Scale, bool Nullable)>();
        await using (var command = new SqlCommand(Sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetByte(4), reader.GetByte(5), reader.GetBoolean(6)));

        return [.. columns.GroupBy(c => c.Type).Select(type =>
            $"IF TYPE_ID(N'{type.Key}') IS NULL CREATE TYPE {type.Key} AS TABLE ({string.Join(", ", type.Select(c => $"[{c.Column}] {SqlType(c.SqlType, c.MaxLength, c.Precision, c.Scale)} {(c.Nullable ? "NULL" : "NOT NULL")}"))})")];
    }

    private static string SqlType(string name, short maxLength, byte precision, byte scale) => name switch
    {
        "nvarchar" or "nchar" => $"{name}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})",
        "varchar" or "char" or "varbinary" or "binary" => $"{name}({(maxLength == -1 ? "max" : maxLength.ToString())})",
        "decimal" or "numeric" => $"{name}({precision}, {scale})",
        "datetime2" or "time" or "datetimeoffset" => $"{name}({scale})",
        _ => name,
    };

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection($"{Server}Database=master;");
        await connection.OpenAsync();
        await using var drop = new SqlCommand($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>Minimal reference data (one location, one property) for catalog tests.</summary>
    private const string SeedCatalogSql = $"""
        DECLARE @now datetime2 = SYSUTCDATETIME(), @empty uniqueidentifier = '00000000-0000-0000-0000-000000000000';
        INSERT INTO Catalog.Destinations (Id, Name, Description, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
        VALUES (NEWID(), 'Test Location', 'A place', 'fuse', @empty, @now, @empty);
        INSERT INTO Catalog.Properties (Id, Name, Description, AddressLine1, TelephoneNumber, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
        VALUES (NEWID(), '{SeedPropertyName}', 'A hostel', '1 Test Street', '000', 'fuse', @empty, @now, @empty);
        INSERT INTO Catalog.Tours (Id, Name, Description, MaxCapacity, MinCapacity, TenantId, CreatedBy, CreatedOn, LastModifiedBy)
        VALUES (NEWID(), 'Seeded Tour', 'A tour', 10, 1, 'fuse', @empty, @now, @empty);
        """;

    [GeneratedRegex(@"\b(CREATE\s+OR\s+ALTER|CREATE|ALTER)\s+PROC(EDURE)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex CreateOrAlter();

}
