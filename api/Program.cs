using System.Security.Claims;
using System.Threading.RateLimiting;
using EntornExamen.Api.Data;
using EntornExamen.Shared.DTOs;
using EntornExamen.Api.Services;
using EntornExamen.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using EntornExamen.Api.Data.Models;

var builder = WebApplication.CreateBuilder(args);

// L'API usa JWT (no DataProtection). Silencia l'avís de claus no persistides.
builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);

// ── Base de dades ─────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["JwtSettings:Secret"]
    ?? throw new InvalidOperationException("JwtSettings:Secret no configurat.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("JwtSettings:Secret ha de tenir almenys 32 caràcters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer           = false,
            ValidateAudience         = false
        };
    });

builder.Services.AddAuthorization();

// ── Serveis ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService,      AuthService>();
builder.Services.AddScoped<IProfessorService, ProfessorService>();
builder.Services.AddScoped<IClassService,     ClassService>();
builder.Services.AddScoped<IEmailService,     EmailService>();
builder.Services.AddScoped<IBackupService,    BackupService>();
builder.Services.AddScoped<IExamenService,    ExamenService>();

// Capçaleres de proxy nginx (Docker): accepta X-Forwarded-For de qualsevol proxy intern
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Entorn Examen: hub de notificació (publicador Redis) ──────────────────────
builder.Services.AddSingleton<ExamenHub>();

// ── Entorn Examen: serveis background ─────────────────────────────────────────
builder.Services.AddHostedService<DhcpMonitorService>();
builder.Services.AddHostedService<DnsMonitorService>();
builder.Services.AddHostedService<SessioCleanupService>();
builder.Services.AddHostedService<CheckinTimeoutService>();

// ── Redis (caché de resultats) ─────────────────────────────────────────────────
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = redisConn;
    opt.InstanceName  = "entornexamen:";
});

// ── Rate limiting (protecció contra força bruta) ───────────────────────────
builder.Services.AddRateLimiter(opt =>
{
    opt.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit         = 10;
        o.Window              = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit          = 0;
    });
    opt.RejectionStatusCode = 429;
});

builder.Services.AddOpenApi();

var app = builder.Build();

// ── Migració i seed automàtics ────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    db.Database.EnsureCreated();

    // ── Taules afegides al model sense migració formal (crea si no existeix) ──
    // Idempotent: segur d'executar a cada arrencada. EnsureCreated crea les taules
    // base si la BD no existeix; aquest bloc afegeix les taules noves a BD ja
    // existents (actualitzacions) sense necessitat de fitxers de migració EF Core.
    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ProfessorLogins')
        BEGIN
            CREATE TABLE [ProfessorLogins] (
                [Id]          INT       NOT NULL IDENTITY(1,1),
                [ProfessorId] INT       NOT NULL,
                [CreatedAt]   DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_ProfessorLogins] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_ProfessorLogins_Professors_ProfessorId]
                    FOREIGN KEY ([ProfessorId]) REFERENCES [Professors]([Id]) ON DELETE CASCADE
            );
            CREATE INDEX [IX_ProfessorLogins_ProfessorId]
                ON [ProfessorLogins] ([ProfessorId]);
            CREATE INDEX [IX_ProfessorLogins_CreatedAt]
                ON [ProfessorLogins] ([CreatedAt]);
        END

        -- ── Entorn Examen ────────────────────────────────────────────────────
        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                       WHERE TABLE_NAME = 'Students' AND COLUMN_NAME = 'Dni')
        BEGIN
            ALTER TABLE [Students] ADD [Dni] NVARCHAR(20) NULL;
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AlumneMacs')
        BEGIN
            CREATE TABLE [AlumneMacs] (
                [Id]            INT           NOT NULL IDENTITY(1,1),
                [StudentId]     INT           NOT NULL,
                [Mac]           NVARCHAR(17)  NOT NULL,
                [Dispositiu]    NVARCHAR(100) NULL,
                [PrimerCopVist] DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_AlumneMacs] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_AlumneMacs_Students_StudentId]
                    FOREIGN KEY ([StudentId]) REFERENCES [Students]([Id]) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX [IX_AlumneMacs_Mac] ON [AlumneMacs] ([Mac]);
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SessionsExamen')
        BEGIN
            CREATE TABLE [SessionsExamen] (
                [Id]            INT           NOT NULL IDENTITY(1,1),
                [ClassId]       INT           NOT NULL,
                [ProfessorId]   INT           NOT NULL,
                [Titol]         NVARCHAR(300) NULL,
                [Descripcio]    NVARCHAR(MAX) NULL,
                [MissatgeActiu] NVARCHAR(MAX) NULL,
                [IniciadaAt]    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                [TancadaAt]     DATETIME2     NULL,
                [Activa]        BIT           NOT NULL DEFAULT 1,
                CONSTRAINT [PK_SessionsExamen] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_SessionsExamen_Classes_ClassId]
                    FOREIGN KEY ([ClassId]) REFERENCES [Classes]([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_SessionsExamen_Professors_ProfessorId]
                    FOREIGN KEY ([ProfessorId]) REFERENCES [Professors]([Id])
            );
            CREATE INDEX [IX_SessionsExamen_ClassId_Activa]
                ON [SessionsExamen] ([ClassId], [Activa]);
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'RegistresConnexio')
        BEGIN
            CREATE TABLE [RegistresConnexio] (
                [Id]              INT          NOT NULL IDENTITY(1,1),
                [SessioId]        INT          NOT NULL,
                [StudentId]       INT          NULL,
                [MacAddress]      NVARCHAR(17) NOT NULL,
                [IpAssignada]     NVARCHAR(45) NULL,
                [ConnectatAt]     DATETIME2    NOT NULL DEFAULT GETUTCDATE(),
                [DesconnectatAt]  DATETIME2    NULL,
                [UltimCheckinAt]  DATETIME2    NULL,
                [Estat]           INT          NOT NULL DEFAULT 3, -- NoConnectat
                CONSTRAINT [PK_RegistresConnexio] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_RegistresConnexio_SessionsExamen_SessioId]
                    FOREIGN KEY ([SessioId]) REFERENCES [SessionsExamen]([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_RegistresConnexio_Students_StudentId]
                    FOREIGN KEY ([StudentId]) REFERENCES [Students]([Id])
            );
            CREATE INDEX [IX_RegistresConnexio_SessioId_Mac]
                ON [RegistresConnexio] ([SessioId], [MacAddress]);
            CREATE INDEX [IX_RegistresConnexio_IpAssignada]
                ON [RegistresConnexio] ([IpAssignada]);
        END
        ELSE IF COL_LENGTH('RegistresConnexio', 'IpAssignada') < 45
        BEGIN
            ALTER TABLE [RegistresConnexio] ALTER COLUMN [IpAssignada] NVARCHAR(45) NULL;
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'PeticiosDns')
        BEGIN
            CREATE TABLE [PeticiosDns] (
                [Id]         INT           NOT NULL IDENTITY(1,1),
                [RegistreId] INT           NOT NULL,
                [Domini]     NVARCHAR(253) NOT NULL,
                [Timestamp]  DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                [EsExterna]  BIT           NOT NULL DEFAULT 0,
                CONSTRAINT [PK_PeticiosDns] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_PeticiosDns_RegistresConnexio_RegistreId]
                    FOREIGN KEY ([RegistreId]) REFERENCES [RegistresConnexio]([Id]) ON DELETE CASCADE
            );
            CREATE INDEX [IX_PeticiosDns_RegistreId_Timestamp]
                ON [PeticiosDns] ([RegistreId], [Timestamp]);
        END
        """);

    // SQL Server Express activa AUTO_CLOSE per defecte: desactivar-lo evita
    // que la BD s'aturi entre peticions i torna a arrencar amb cada connexió nova.
    try
    {
        var dbName = db.Database.GetDbConnection().Database;
#pragma warning disable EF1002 // dbName prové de la cadena de connexió, no de l'usuari
        await db.Database.ExecuteSqlRawAsync($"ALTER DATABASE [{dbName}] SET AUTO_CLOSE OFF");
#pragma warning restore EF1002
    }
    catch { /* ignora si no té permisos o si ja està desactivat */ }
    await SeedData.InitializeAsync(db, config);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // disponible a /openapi/v1.json
}

// Llegeix X-Real-IP / X-Forwarded-For des del proxy nginx (Docker).
// KnownNetworks/KnownProxies buits → accepta de qualsevol proxy (xarxa Docker controlada).
app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// ── Helpers locals ────────────────────────────────────────────────────────────
static int GetUserId(ClaimsPrincipal user) =>
    int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

static bool IsAdmin(ClaimsPrincipal user) =>
    user.IsInRole("Admin");

static bool IsProfessor(ClaimsPrincipal user) =>
    user.IsInRole("Professor") || user.IsInRole("Admin");

// ════════════════════════════════════════════════════════════════════════════
// AUTENTICACIÓ
// ════════════════════════════════════════════════════════════════════════════

app.MapPost("/api/auth/professor", async (ProfessorLoginRequest req, IAuthService svc) =>
{
    var result = await svc.ProfessorLoginAsync(req);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/request-reset", async (
    PasswordResetRequestDto req, AppDbContext db, IEmailService email,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache cache) =>
{
    // Sempre retorna Ok per no revelar si el correu existeix
    var prof = await db.Professors.FirstOrDefaultAsync(
        p => p.Email == req.Email.Trim().ToLower());
    if (prof is not null && email.IsEnabled)
    {
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        await cache.SetStringAsync($"entornexamen:reset:{req.Email.Trim().ToLower()}", code,
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });
        await email.SendPasswordResetAsync(prof.Email, prof.NomComplet, code);
    }
    return Results.Ok(new { message = "Si el correu existeix, rebràs el codi en breu." });
}).RequireRateLimiting("auth");

app.MapPost("/api/auth/confirm-reset", async (
    PasswordResetConfirmDto req, AppDbContext db,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache cache) =>
{
    var email = req.Email.Trim().ToLower();
    var stored = await cache.GetStringAsync($"entornexamen:reset:{email}");
    if (stored is null || stored != req.Code.Trim())
        return Results.BadRequest(new { error = "Codi incorrecte o expirat." });
    if (req.NewPassword.Length < 8)
        return Results.BadRequest(new { error = "La contrasenya ha de tenir almenys 8 caràcters." });

    var prof = await db.Professors.FirstOrDefaultAsync(p => p.Email == email);
    if (prof is null) return Results.NotFound();

    prof.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
    await db.SaveChangesAsync();
    await cache.RemoveAsync($"entornexamen:reset:{email}");
    return Results.Ok(new { message = "Contrasenya actualitzada correctament." });
}).RequireRateLimiting("auth");

// ════════════════════════════════════════════════════════════════════════════
// PROFESSORS  (Admin only per a escriptura)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/professors", async (IProfessorService svc) =>
    Results.Ok(await svc.GetAllAsync()))
    .RequireAuthorization();

app.MapGet("/api/professors/{id:int}", async (int id, IProfessorService svc) =>
{
    var p = await svc.GetByIdAsync(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

app.MapPost("/api/professors", async (CreateProfessorRequest req, IProfessorService svc,
    ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var p = await svc.CreateAsync(req);
    return Results.Created($"/api/professors/{p.Id}", p);
}).RequireAuthorization();

app.MapPut("/api/professors/{id:int}", async (int id, UpdateProfessorRequest req,
    IProfessorService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var p = await svc.UpdateAsync(id, req);
    return p is null ? Results.NotFound() : Results.Ok(p);
}).RequireAuthorization();

app.MapDelete("/api/professors/{id:int}", async (int id, IProfessorService svc,
    ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    try
    {
        var ok = await svc.DeleteAsync(id);
        return ok ? Results.NoContent() : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/professors/me", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var prof = await db.Professors.FindAsync(GetUserId(user));
    if (prof is null) return Results.NotFound();
    return Results.Ok(new ProfessorDto(prof.Id, prof.Email, prof.Nom, prof.Cognoms,
        prof.NomComplet, prof.IsAdmin, prof.CreatedAt));
}).RequireAuthorization();

app.MapPut("/api/professors/me", async (UpdateOwnProfileRequest req, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var prof = await db.Professors.FindAsync(GetUserId(user));
    if (prof is null) return Results.NotFound();

    // Verifica contrasenya actual si es vol canviar la contrasenya
    if (!string.IsNullOrWhiteSpace(req.NewPassword))
    {
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) ||
            !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, prof.PasswordHash))
            return Results.BadRequest(new { error = "La contrasenya actual és incorrecta." });
        if (req.NewPassword.Length < 8)
            return Results.BadRequest(new { error = "La nova contrasenya ha de tenir almenys 8 caràcters." });
        prof.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
    }

    prof.Nom     = req.Nom.Trim();
    prof.Cognoms = req.Cognoms.Trim();
    await db.SaveChangesAsync();
    return Results.Ok(new ProfessorDto(prof.Id, prof.Email, prof.Nom, prof.Cognoms,
        prof.NomComplet, prof.IsAdmin, prof.CreatedAt));
}).RequireAuthorization();

app.MapPost("/api/professors/{professorId:int}/send-credentials", async (
    int professorId, IProfessorService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.SendCredentialsAsync(professorId);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/professors/send-all-credentials", async (
    IProfessorService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.SendAllCredentialsAsync();
    return Results.Ok(result);
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// CLASSES  (lectura per a tots els professors; escriptura admin only)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/classes", async (IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    return Results.Ok(await svc.GetAllAsync());
}).RequireAuthorization();

app.MapGet("/api/classes/{id:int}", async (int id, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var c = await svc.GetByIdAsync(id);
    return c is null ? Results.NotFound() : Results.Ok(c);
}).RequireAuthorization();

app.MapPost("/api/classes", async (CreateClassRequest req, IClassService svc,
    ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var c = await svc.CreateAsync(req);
    return Results.Created($"/api/classes/{c.Id}", c);
}).RequireAuthorization();

app.MapPut("/api/classes/{id:int}", async (int id, UpdateClassRequest req,
    IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var c = await svc.UpdateAsync(id, req);
    return c is null ? Results.NotFound() : Results.Ok(c);
}).RequireAuthorization();

app.MapDelete("/api/classes/{id:int}", async (int id, IClassService svc,
    ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var ok = await svc.DeleteAsync(id);
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

// ── Alumnes dins d'una classe ─────────────────────────────────────────────────

app.MapGet("/api/classes/{classId:int}/students", async (int classId,
    IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var list = await svc.GetStudentsAsync(classId);
    return Results.Ok(list);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students", async (int classId,
    CreateStudentRequest req, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var s = await svc.AddStudentAsync(classId, req);
    return Results.Created($"/api/classes/{classId}/students/{s.Id}", s);
}).RequireAuthorization();

app.MapPut("/api/classes/{classId:int}/students/{studentId:int}", async (
    int classId, int studentId, UpdateStudentRequest req,
    IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var s = await svc.UpdateStudentAsync(classId, studentId, req);
    return s is null ? Results.NotFound() : Results.Ok(s);
}).RequireAuthorization();

app.MapDelete("/api/classes/{classId:int}/students/{studentId:int}", async (
    int classId, int studentId, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var ok = await svc.DeleteStudentAsync(classId, studentId);
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/bulk", async (
    int classId, BulkCreateStudentsRequest req, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.BulkAddStudentsAsync(classId, req);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/{studentId:int}/move", async (
    int classId, int studentId, MoveStudentRequest req, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var s = await svc.MoveStudentAsync(classId, studentId, req.TargetClassId);
    return s is null ? Results.NotFound() : Results.Ok(s);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/{studentId:int}/foto", async (
    int classId, int studentId, HttpRequest httpReq,
    AppDbContext db, IExamenService svc, IWebHostEnvironment env, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    if (!httpReq.HasFormContentType) return Results.BadRequest(new { error = "Cal multipart/form-data." });
    var exists = await db.Students.AnyAsync(s => s.Id == studentId && s.ClassId == classId);
    if (!exists) return Results.NotFound();
    var form = await httpReq.ReadFormAsync();
    var file = form.Files.GetFile("foto");
    if (file is null) return Results.BadRequest(new { error = "Camp 'foto' no trobat." });
    using var stream = file.OpenReadStream();
    await svc.UploadStudentFotoAsync(studentId, stream, env.WebRootPath);
    return Results.Ok(new { url = $"/fotos/alumnes/{studentId}.jpg" });
}).RequireAuthorization();


// ════════════════════════════════════════════════════════════════════════════
// HEALTH CHECK
// ════════════════════════════════════════════════════════════════════════════

// ── Health check ──────────────────────────────────────────────────────────────
app.MapGet("/api/health", async (AppDbContext db, StackExchange.Redis.IConnectionMultiplexer redis) =>
{
    var dbOk    = false;
    var redisOk = false;
    try { dbOk    = await db.Database.CanConnectAsync(); }    catch { }
    try { redisOk = redis.IsConnected; }                      catch { }
    var status = dbOk && redisOk ? "ok" : "degraded";
    return Results.Ok(new { status, db = dbOk ? "ok" : "error", redis = redisOk ? "ok" : "error" });
}); // Públic: permet monitoratge extern i Docker healthchecks sense token

// ════════════════════════════════════════════════════════════════════════════
// ESTADÍSTIQUES D'ÚS (admin only)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/admin/stats", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();

    var since30  = DateTime.UtcNow.AddDays(-30);
    var since6mo = DateTime.UtcNow.AddMonths(-6);

    var professors = await db.Professors
        .OrderBy(p => p.Cognoms).ThenBy(p => p.Nom)
        .ToListAsync();

    var loginStats = await db.ProfessorLogins
        .GroupBy(l => l.ProfessorId)
        .Select(g => new {
            ProfessorId = g.Key,
            Last30      = g.Count(l => l.CreatedAt >= since30),
            LastAccess  = (DateTime?)g.Max(l => l.CreatedAt)
        })
        .ToListAsync();

    var monthlyLogins = (await db.ProfessorLogins
        .Where(l => l.CreatedAt >= since6mo)
        .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .OrderBy(m => m.Year).ThenBy(m => m.Month)
        .ToListAsync())
        .Select(m => new MonthlyStatDto(m.Year, m.Month, m.Count))
        .ToList();

    var stats = professors.Select(p =>
    {
        var login = loginStats.FirstOrDefault(l => l.ProfessorId == p.Id);
        return new ProfessorStatsDto(
            p.Id, p.NomComplet, p.Email, p.IsAdmin,
            login?.Last30 ?? 0,
            login?.LastAccess);
    }).ToList();

    return Results.Ok(new AdminStatsDto(stats, monthlyLogins));
}).RequireAuthorization();

app.MapDelete("/api/admin/stats/logins", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    await db.ProfessorLogins.ExecuteDeleteAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/admin/diagnostic", async (AppDbContext db, IConfiguration config, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();

    var dhcpPath = config["Examen:DhcpLeasesPath"] ?? "/data/dhcpd.leases";
    var dnsPath  = config["Examen:DnsLogPath"]     ?? "/data/dns-queries.log";

    // DHCP
    string? dhcpError = null;
    string? dhcpLastLine = null;
    long    dhcpBytes = 0;
    DateTime? dhcpModified = null;
    try
    {
        var fi = new FileInfo(dhcpPath);
        dhcpBytes    = fi.Exists ? fi.Length : -1;
        dhcpModified = fi.Exists ? fi.LastWriteTimeUtc : null;
        if (fi.Exists && fi.Length > 0)
        {
            using var fs = new FileStream(dhcpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(Math.Max(0, fs.Length - 8192), SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            var tail = await sr.ReadToEndAsync();
            var blocs = System.Text.RegularExpressions.Regex.Matches(tail,
                @"lease\s+([\d\.]+)\s*\{([^}]*)\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            var actives = blocs.Cast<System.Text.RegularExpressions.Match>()
                .Where(b => System.Text.RegularExpressions.Regex.IsMatch(
                    b.Groups[2].Value, @"binding state\s+active;",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();
            if (actives.Count > 0)
            {
                var ult = actives[^1];
                var macM = System.Text.RegularExpressions.Regex.Match(
                    ult.Groups[2].Value, @"hardware ethernet\s+([\da-fA-F:]{17});");
                dhcpLastLine = $"{actives.Count} concessions actives; última: {ult.Groups[1].Value}" +
                    (macM.Success ? $" ({macM.Groups[1].Value})" : "");
            }
            else
            {
                dhcpLastLine = blocs.Count > 0
                    ? $"{blocs.Count} concessions trobades al fitxer, cap activa"
                    : "Cap concessió trobada al fitxer";
            }
        }
    }
    catch (Exception ex) { dhcpError = ex.Message; }

    // DNS
    string? dnsError = null;
    string? dnsLastLine = null;
    long    dnsBytes = 0;
    DateTime? dnsModified = null;
    try
    {
        var fi = new FileInfo(dnsPath);
        dnsBytes    = fi.Exists ? fi.Length : -1;
        dnsModified = fi.Exists ? fi.LastWriteTimeUtc : null;
        if (fi.Exists && fi.Length > 0)
        {
            using var fs = new FileStream(dnsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(Math.Max(0, fs.Length - 1024), SeekOrigin.Begin);
            using var sr = new StreamReader(fs);
            var tail = await sr.ReadToEndAsync();
            dnsLastLine = tail.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        }
    }
    catch (Exception ex) { dnsError = ex.Message; }

    // BD
    var sessionsActives = await db.SessionsExamen.CountAsync(s => s.Activa);
    var registresActius = await db.RegistresConnexio
        .CountAsync(r => r.Estat != EntornExamen.Api.Data.Models.EstatConnexio.Desconnectat);
    var ultimCheckin    = await db.RegistresConnexio
        .Where(r => r.UltimCheckinAt.HasValue)
        .OrderByDescending(r => r.UltimCheckinAt)
        .Select(r => r.UltimCheckinAt)
        .FirstOrDefaultAsync();

    return Results.Ok(new DiagnosticDto(
        new DiagnosticFitxer(dhcpPath, dhcpBytes >= 0, dhcpBytes, dhcpModified, dhcpLastLine, dhcpError),
        new DiagnosticFitxer(dnsPath,  dnsBytes  >= 0, dnsBytes,  dnsModified,  dnsLastLine,  dnsError),
        new DiagnosticBd(sessionsActives, registresActius, ultimCheckin)
    ));
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// BACKUP / RESTORE (admin only)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/admin/backup/export", async (IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var backup   = await svc.ExportAsync();
    var json     = System.Text.Json.JsonSerializer.Serialize(backup,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    var bytes    = System.Text.Encoding.UTF8.GetBytes(json);
    var fileName = $"entornexamen_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
    return Results.File(bytes, "application/json", fileName);
}).RequireAuthorization();

app.MapPost("/api/admin/backup/import", async (
    BackupDto backup, IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.ImportAsync(backup);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization();

app.MapGet("/api/admin/backup/files", async (IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    return Results.Ok(await svc.ListFilesAsync());
}).RequireAuthorization();

app.MapPost("/api/admin/backup/files", async (IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var info = await svc.CreateFileAsync();
    return Results.Ok(info);
}).RequireAuthorization();

app.MapGet("/api/admin/backup/files/{name}", async (
    string name, IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.DownloadFileAsync(name);
    if (result is null) return Results.NotFound();
    return Results.File(result.Value.Data, "application/json", result.Value.Name);
}).RequireAuthorization();

app.MapDelete("/api/admin/backup/files/{name}", async (
    string name, IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    return await svc.DeleteFileAsync(name) ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/admin/backup/files/{name}/restore", async (
    string name, IBackupService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.RestoreFileAsync(name);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// ENTORN EXAMEN
// ════════════════════════════════════════════════════════════════════════════

// ── Sessions d'examen ─────────────────────────────────────────────────────────
app.MapGet("/api/examen/sessions", async (IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var list = await svc.GetSessionsAsync(GetUserId(user), IsAdmin(user));
    return Results.Ok(list);
}).RequireAuthorization();

app.MapPost("/api/examen/sessions", async (CreateSessioRequest req, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (sessio, error) = await svc.CreateSessioAsync(req, GetUserId(user));
    if (error is not null) return Results.Conflict(new { error });
    return Results.Created($"/api/examen/sessions/{sessio!.Id}", sessio);
}).RequireAuthorization();

app.MapGet("/api/examen/sessions/{id:int}", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var dashboard = await svc.GetDashboardAsync(id, GetUserId(user), IsAdmin(user));
    return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
}).RequireAuthorization();

app.MapPut("/api/examen/sessions/{id:int}/tancar", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.TancarSessioAsync(id, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.BadRequest(new { error });
}).RequireAuthorization();

app.MapPut("/api/examen/sessions/{id:int}/reobrir", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.ReobrirSessioAsync(id, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.Conflict(new { error });
}).RequireAuthorization();

app.MapDelete("/api/examen/sessions/{id:int}", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.EliminarSessioAsync(id, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.BadRequest(new { error });
}).RequireAuthorization();

app.MapPut("/api/examen/sessions/{id:int}/missatge", async (int id, MissatgeRequest req,
    IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.SetMissatgeAsync(id, req.Text, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound(new { error });
}).RequireAuthorization();

app.MapDelete("/api/examen/sessions/{id:int}/missatge", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.SetMissatgeAsync(id, null, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound(new { error });
}).RequireAuthorization();

app.MapGet("/api/examen/sessions/{id:int}/exportar", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var result = await svc.ExportarCsvAsync(id, GetUserId(user), IsAdmin(user));
    if (result is null) return Results.NotFound();
    var (bytes, fileName) = result.Value;
    return Results.File(bytes, "text/csv; charset=utf-8", fileName);
}).RequireAuthorization();

app.MapGet("/api/examen/sessions/{id:int}/dashboard", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var dashboard = await svc.GetDashboardAsync(id, GetUserId(user), IsAdmin(user));
    return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
}).RequireAuthorization();

// ── Check-in alumne (sense autenticació — xarxa local tancada) ────────────────
// No s'aplica rate limiting: els alumnes fan check-in periòdicament (N/minut per classe)
// i la xarxa d'examen és una WiFi aïllada sense accés extern.
app.MapPost("/api/examen/checkin", async (CheckinRequest req, IExamenService svc, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "L'email és obligatori." });
    // ForwardedHeaders ha actualitzat Connection.RemoteIpAddress → IP real del client
    var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    var (resp, error) = await svc.CheckinAsync(req, clientIp);
    return error is not null
        ? Results.UnprocessableEntity(new { error })
        : Results.Ok(resp);
});

// ── Sortida voluntària alumne ──────────────────────────────────────────────────
app.MapPost("/api/examen/sortida", async (IExamenService svc, HttpContext ctx) =>
{
    var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
    var (ok, error) = await svc.SortirAsync(clientIp);
    return ok ? Results.Ok() : Results.NotFound(new { error });
});

// ── Expulsar alumne (professor) ────────────────────────────────────────────────
app.MapPost("/api/examen/sessions/{sessioId:int}/alumnes/{studentId:int}/expulsar",
    async (int sessioId, int studentId, IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var (ok, error) = await svc.ExpulsarAsync(sessioId, studentId, GetUserId(user), IsAdmin(user));
    return ok ? Results.Ok() : Results.NotFound(new { error });
}).RequireAuthorization();

// ── Events DHCP (cridat des del hook del sistema host) ────────────────────────
app.MapPost("/api/examen/dhcp/event", async (DhcpEventRequest req, IExamenService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Mac) || string.IsNullOrWhiteSpace(req.Event))
        return Results.BadRequest();
    await svc.ProcessDhcpEventAsync(req);
    return Results.Ok();
});

// ── Events DNS ────────────────────────────────────────────────────────────────
app.MapPost("/api/examen/dns/event", async (DnsEventRequest req, IExamenService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Ip) || string.IsNullOrWhiteSpace(req.Domini))
        return Results.BadRequest();
    await svc.ProcessDnsEventAsync(req);
    return Results.Ok();
});

// ── Importació alumnes ────────────────────────────────────────────────────────
app.MapPost("/api/examen/importar-alumnes", async (HttpRequest httpReq,
    IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    if (!httpReq.HasFormContentType) return Results.BadRequest(new { error = "Cal multipart/form-data." });
    var form = await httpReq.ReadFormAsync();
    var fitxer = form.Files.GetFile("fitxer");
    if (fitxer is null) return Results.BadRequest(new { error = "Camp 'fitxer' no trobat." });

    using var stream = fitxer.OpenReadStream();
    var (result, error) = await svc.ImportarAlumnesAsync(stream, GetUserId(user), IsAdmin(user));
    return error is not null
        ? Results.BadRequest(new { error })
        : Results.Ok(result);
}).RequireAuthorization();

// ── Importació alumnes XLS (format EPSS natiu, per classe) ───────────────────
app.MapPost("/api/examen/importar-alumnes-xls", async (HttpRequest httpReq,
    [Microsoft.AspNetCore.Mvc.FromQuery] int classId,
    IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    if (classId <= 0) return Results.BadRequest(new { error = "Cal indicar classId." });
    if (!httpReq.HasFormContentType) return Results.BadRequest(new { error = "Cal multipart/form-data." });
    var form = await httpReq.ReadFormAsync();
    var fitxer = form.Files.GetFile("fitxer");
    if (fitxer is null) return Results.BadRequest(new { error = "Camp 'fitxer' no trobat." });

    using var stream = fitxer.OpenReadStream();
    var (result, error) = await svc.ImportarAlumnesXlsAsync(stream, classId, IsAdmin(user));
    return error is not null
        ? Results.BadRequest(new { error })
        : Results.Ok(result);
}).RequireAuthorization();

// ── MACs ──────────────────────────────────────────────────────────────────────
app.MapGet("/api/examen/macs", async (IExamenService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var macs = await svc.GetMacsAsync(true);
    return Results.Ok(macs);
}).RequireAuthorization();

app.MapDelete("/api/examen/macs/{id:int}", async (int id, IExamenService svc,
    ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    return await svc.DeleteMacAsync(id, true)
        ? Results.NoContent()
        : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/examen/importar-fotos", async (HttpRequest httpReq,
    IExamenService svc, IConfiguration config, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    if (!httpReq.HasFormContentType) return Results.BadRequest(new { error = "Cal multipart/form-data." });
    var form = await httpReq.ReadFormAsync();
    var zip  = form.Files.GetFile("zip");
    if (zip is null) return Results.BadRequest(new { error = "Camp 'zip' no trobat." });

    var wwwrootPath = config["Examen:WebWwwrootPath"] ?? "/app/wwwroot";
    using var stream = zip.OpenReadStream();
    var (result, error) = await svc.ImportarFotosAsync(stream, wwwrootPath);
    return error is not null
        ? Results.BadRequest(new { error })
        : Results.Ok(result);
}).RequireAuthorization();

app.Run();
