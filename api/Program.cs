using System.Security.Claims;
using System.Threading.RateLimiting;
using AutoCo.Api.Data;
using AutoCo.Shared.DTOs;
using AutoCo.Api.Services;
using AutoCo.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AutoCo.Api.Data.Models;

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
builder.Services.AddScoped<IAuthService,       AuthService>();
builder.Services.AddScoped<IProfessorService,  ProfessorService>();
builder.Services.AddScoped<IClassService,      ClassService>();
builder.Services.AddScoped<IModuleService,     ModuleService>();
builder.Services.AddScoped<IActivityService,   ActivityService>();
builder.Services.AddScoped<IEvaluationService, EvaluationService>();
builder.Services.AddScoped<IResultsService,    ResultsService>();
builder.Services.AddScoped<IEmailService,      EmailService>();
builder.Services.AddScoped<IBackupService,     BackupService>();
builder.Services.AddScoped<IExamenService,     ExamenService>();

// ── Entorn Examen: hub de notificació (publicador Redis) ──────────────────────
builder.Services.AddSingleton<ExamenHub>();

// ── Entorn Examen: serveis background ─────────────────────────────────────────
builder.Services.AddHostedService<DhcpMonitorService>();
builder.Services.AddHostedService<DnsMonitorService>();

// ── Redis (caché de resultats) ─────────────────────────────────────────────────
var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConn));
builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = redisConn;
    opt.InstanceName  = "autoco:";
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
    db.Database.Migrate();

    // ── Taules afegides al model sense migració formal (crea si no existeix) ──
    // Idempotent: segur d'executar a cada arrencada. Necessari quan el projecte
    // no té fitxers de migració i la BD va ser creada amb EnsureCreated o bé
    // amb una versió anterior del model.
    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ActivityCriteria')
        BEGIN
            CREATE TABLE [ActivityCriteria] (
                [Id]         INT          NOT NULL IDENTITY(1,1),
                [ActivityId] INT          NOT NULL,
                [Key]        NVARCHAR(50) NOT NULL,
                [Label]      NVARCHAR(200) NOT NULL,
                [OrderIndex] INT          NOT NULL,
                CONSTRAINT [PK_ActivityCriteria] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_ActivityCriteria_Activities_ActivityId]
                    FOREIGN KEY ([ActivityId]) REFERENCES [Activities]([Id]) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX [IX_ActivityCriteria_ActivityId_Key]
                ON [ActivityCriteria] ([ActivityId], [Key]);
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ProfessorNotes')
        BEGIN
            CREATE TABLE [ProfessorNotes] (
                [Id]         INT           NOT NULL IDENTITY(1,1),
                [ActivityId] INT           NOT NULL,
                [StudentId]  INT           NOT NULL,
                [Note]       NVARCHAR(MAX) NOT NULL,
                [UpdatedAt]  DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_ProfessorNotes] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_ProfessorNotes_Activities_ActivityId]
                    FOREIGN KEY ([ActivityId]) REFERENCES [Activities]([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_ProfessorNotes_Students_StudentId]
                    FOREIGN KEY ([StudentId]) REFERENCES [Students]([Id])
            );
            CREATE UNIQUE INDEX [IX_ProfessorNotes_ActivityId_StudentId]
                ON [ProfessorNotes] ([ActivityId], [StudentId]);
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ActivityTemplates')
        BEGIN
            CREATE TABLE [ActivityTemplates] (
                [Id]          INT           NOT NULL IDENTITY(1,1),
                [ProfessorId] INT           NOT NULL,
                [Name]        NVARCHAR(300) NOT NULL,
                [Description] NVARCHAR(MAX) NULL,
                [CriteriaJson] NVARCHAR(MAX) NOT NULL DEFAULT N'[]',
                [CreatedAt]   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_ActivityTemplates] PRIMARY KEY ([Id])
            );
            CREATE INDEX [IX_ActivityTemplates_ProfessorId]
                ON [ActivityTemplates] ([ProfessorId]);
        END

        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ActivityLogs')
        BEGIN
            CREATE TABLE [ActivityLogs] (
                [Id]           INT           NOT NULL IDENTITY(1,1),
                [ActivityId]   INT           NOT NULL,
                [ActivityName] NVARCHAR(300) NOT NULL,
                [ActorName]    NVARCHAR(300) NULL,
                [Action]       NVARCHAR(50)  NOT NULL,
                [Details]      NVARCHAR(MAX) NULL,
                [CreatedAt]    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_ActivityLogs] PRIMARY KEY ([Id])
            );
            CREATE INDEX [IX_ActivityLogs_ActivityId]
                ON [ActivityLogs] ([ActivityId]);
        END

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
                [IpAssignada]     NVARCHAR(15) NULL,
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

app.MapPost("/api/auth/student", async (StudentLoginRequest req, IAuthService svc) =>
{
    var result = await svc.StudentLoginAsync(req);
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
        await cache.SetStringAsync($"autoco:reset:{req.Email.Trim().ToLower()}", code,
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
    var stored = await cache.GetStringAsync($"autoco:reset:{email}");
    if (stored is null || stored != req.Code.Trim())
        return Results.BadRequest(new { error = "Codi incorrecte o expirat." });
    if (req.NewPassword.Length < 8)
        return Results.BadRequest(new { error = "La contrasenya ha de tenir almenys 8 caràcters." });

    var prof = await db.Professors.FirstOrDefaultAsync(p => p.Email == email);
    if (prof is null) return Results.NotFound();

    prof.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
    await db.SaveChangesAsync();
    await cache.RemoveAsync($"autoco:reset:{email}");
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

app.MapPost("/api/classes/{classId:int}/students/{studentId:int}/reset-password", async (
    int classId, int studentId, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.ResetPasswordAsync(classId, studentId);
    return result is null ? Results.NotFound() : Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/{studentId:int}/send-password", async (
    int classId, int studentId, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.SendPasswordAsync(classId, studentId);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/send-all-passwords", async (
    int classId, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var result = await svc.SendAllPasswordsAsync(classId);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/students/{studentId:int}/move", async (
    int classId, int studentId, MoveStudentRequest req, IClassService svc, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    var s = await svc.MoveStudentAsync(classId, studentId, req.TargetClassId);
    return s is null ? Results.NotFound() : Results.Ok(s);
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// MÒDULS
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/classes/{classId:int}/modules", async (int classId,
    IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var list = await svc.GetByClassAsync(classId);
    return Results.Ok(list);
}).RequireAuthorization();

app.MapGet("/api/classes/{classId:int}/modules/{id:int}", async (int classId, int id,
    IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var m = await svc.GetByIdAsync(id, GetUserId(user), IsAdmin(user));
    return m is null ? Results.NotFound() : Results.Ok(m);
}).RequireAuthorization();

app.MapPost("/api/classes/{classId:int}/modules", async (int classId,
    CreateModuleRequest req, IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    try
    {
        var m = await svc.CreateAsync(classId, GetUserId(user), req);
        return Results.Created($"/api/classes/{classId}/modules/{m.Id}", m);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/classes/{classId:int}/modules/{id:int}", async (int classId, int id,
    UpdateModuleRequest req, IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var m = await svc.UpdateAsync(id, GetUserId(user), IsAdmin(user), req);
    return m is null ? Results.NotFound() : Results.Ok(m);
}).RequireAuthorization();

app.MapDelete("/api/classes/{classId:int}/modules/{id:int}", async (int classId, int id,
    IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.DeleteAsync(id, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

// ── Exclusions de mòdul ───────────────────────────────────────────────────────

app.MapGet("/api/modules/{moduleId:int}/exclusions", async (int moduleId,
    IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var list = await svc.GetExclusionsAsync(moduleId, GetUserId(user), IsAdmin(user));
    return Results.Ok(list);
}).RequireAuthorization();

app.MapPost("/api/modules/{moduleId:int}/exclusions/{studentId:int}", async (
    int moduleId, int studentId, IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.AddExclusionAsync(moduleId, studentId, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapDelete("/api/modules/{moduleId:int}/exclusions/{studentId:int}", async (
    int moduleId, int studentId, IModuleService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.RemoveExclusionAsync(moduleId, studentId, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// ACTIVITATS
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/activities", async (IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var profId = IsProfessor(user) && !IsAdmin(user) ? GetUserId(user) : (int?)null;
    return Results.Ok(await svc.GetAllAsync(profId));
}).RequireAuthorization();

app.MapGet("/api/activities/{id:int}", async (int id, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var profId = IsProfessor(user) && !IsAdmin(user) ? GetUserId(user) : (int?)null;
    var a = await svc.GetByIdAsync(id, profId);
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

app.MapPost("/api/activities", async (CreateActivityRequest req, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    try
    {
        var a = await svc.CreateAsync(GetUserId(user), IsAdmin(user), req);
        return Results.Created($"/api/activities/{a.Id}", a);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/activities/{id:int}/duplicate", async (int id, DuplicateActivityRequest req,
    IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    try
    {
        var a = await svc.DuplicateAsync(id, GetUserId(user), IsAdmin(user), req);
        return Results.Created($"/api/activities/{a.Id}", a);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/activities/{id:int}/duplicate-cross", async (int id, DuplicateCrossRequest req,
    IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    try
    {
        var a = await svc.DuplicateCrossAsync(id, GetUserId(user), IsAdmin(user), req);
        return Results.Created($"/api/activities/{a.Id}", a);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/activities/{id:int}/participation", async (int id, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var p = await svc.GetParticipationAsync(id, GetUserId(user), IsAdmin(user));
    return Results.Ok(p);
}).RequireAuthorization();

app.MapPost("/api/activities/{id:int}/remind", async (int id, IActivityService svc,
    IEmailService email, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var result = await svc.SendRemindersAsync(id, GetUserId(user), IsAdmin(user), email);
    return Results.Ok(result);
}).RequireAuthorization();

app.MapGet("/api/activities/{id:int}/criteria", async (int id, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var list = await svc.GetCriteriaAsync(id, GetUserId(user), IsAdmin(user));
    return Results.Ok(list);
}).RequireAuthorization();

app.MapPut("/api/activities/{id:int}/criteria", async (int id, SaveCriteriaRequest req,
    IActivityService svc, IResultsService results, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    if (!req.Items.Any()) return Results.BadRequest(new { error = "Cal almenys un criteri." });
    if (req.Items.Count > 50) return Results.BadRequest(new { error = "No es poden desar més de 50 criteris per activitat." });
    var list = await svc.SaveCriteriaAsync(id, GetUserId(user), IsAdmin(user), req);
    await results.InvalidateCacheAsync(id); // criteris canviats → resultats i gràfica desactualitzats
    return Results.Ok(list);
}).RequireAuthorization();

app.MapGet("/api/activities/{id:int}/groups/export", async (int id, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var result = await svc.ExportGroupsAsync(id, GetUserId(user), IsAdmin(user));
    if (result is null) return Results.NotFound();
    var (bytes, fileName) = result.Value;
    return Results.File(bytes, "text/csv; charset=utf-8", fileName);
}).RequireAuthorization();

app.MapPost("/api/activities/{id:int}/groups/import", async (int id, ImportGroupsRequest req,
    IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    try
    {
        var result = await svc.ImportGroupsAsync(id, GetUserId(user), IsAdmin(user), req.CsvContent);
        return Results.Ok(result);
    }
    catch (UnauthorizedAccessException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/activities/{id:int}", async (int id, UpdateActivityRequest req,
    IActivityService svc, IResultsService results, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var a = await svc.UpdateAsync(id, GetUserId(user), IsAdmin(user), req);
    if (a is not null) await results.InvalidateCacheAsync(id); // nom/descripció canviats → caché stale
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

app.MapDelete("/api/activities/{id:int}", async (int id, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.DeleteAsync(id, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/activities/{id:int}/toggle", async (int id, IActivityService svc,
    IResultsService results, AppDbContext db, ClaimsPrincipal user, ILogger<Program> logger) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var a = await svc.ToggleOpenAsync(id, GetUserId(user), IsAdmin(user));
    if (a is not null)
    {
        await results.InvalidateCacheAsync(id); // IsOpen canvia → caché de resultats stale
        try
        {
            var prof = await db.Professors.FindAsync(GetUserId(user));
            db.ActivityLogs.Add(new ActivityLog
            {
                ActivityId   = id,
                ActivityName = a.Name,
                ActorName    = prof?.NomComplet,
                Action       = a.IsOpen ? "opened" : "closed",
                CreatedAt    = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { logger.LogWarning(ex, "Error desant log de toggle (activitat {Id})", id); }
    }
    return a is null ? Results.NotFound() : Results.Ok(a);
}).RequireAuthorization();

// ── Grups ─────────────────────────────────────────────────────────────────────

app.MapGet("/api/activities/{actId:int}/groups", async (int actId, IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var groups = await svc.GetGroupsAsync(actId, GetUserId(user), IsAdmin(user));
    return groups is null ? Results.Forbid() : Results.Ok(groups);
}).RequireAuthorization();

app.MapPost("/api/activities/{actId:int}/groups", async (int actId,
    CreateGroupRequest req, IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var g = await svc.CreateGroupAsync(actId, req, GetUserId(user), IsAdmin(user));
    return g is null ? Results.Forbid()
                     : Results.Created($"/api/activities/{actId}/groups/{g.Id}", g);
}).RequireAuthorization();

app.MapPut("/api/activities/{actId:int}/groups/{groupId:int}", async (
    int actId, int groupId, RenameGroupRequest req,
    IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
    var ok = await svc.RenameGroupAsync(actId, groupId, req.Name, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapDelete("/api/activities/{actId:int}/groups/{groupId:int}", async (
    int actId, int groupId, IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.DeleteGroupAsync(actId, groupId, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/activities/{actId:int}/groups/{groupId:int}/members", async (
    int actId, int groupId, AddMemberRequest req,
    IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.AddMemberAsync(actId, groupId, req.StudentId, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.BadRequest();
}).RequireAuthorization();

app.MapPut("/api/activities/{actId:int}/groups/reorder", async (
    int actId, ReorderGroupsRequest req, IActivityService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.ReorderGroupsAsync(actId, req.OrderedGroupIds, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.Forbid();
}).RequireAuthorization();

app.MapDelete("/api/activities/{actId:int}/groups/{groupId:int}/members/{studentId:int}",
    async (int actId, int groupId, int studentId, IActivityService svc,
    ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var ok = await svc.RemoveMemberAsync(actId, groupId, studentId, GetUserId(user), IsAdmin(user));
    return ok ? Results.NoContent() : Results.NotFound();
}).RequireAuthorization();

// ── Dashboard alumne ──────────────────────────────────────────────────────────

app.MapGet("/api/student/activities", async (IActivityService svc, ClaimsPrincipal user) =>
{
    if (!user.IsInRole("Student")) return Results.Forbid();
    var classId = int.Parse(user.FindFirstValue("classId")!);
    var list    = await svc.GetStudentActivitiesAsync(GetUserId(user), classId);
    return Results.Ok(new StudentDashboardDto(list));
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// AVALUACIONS
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/evaluations/{activityId:int}", async (int activityId,
    IEvaluationService svc, ClaimsPrincipal user) =>
{
    if (!user.IsInRole("Student")) return Results.Forbid();
    var form = await svc.GetFormAsync(activityId, GetUserId(user));
    return form is null ? Results.NotFound() : Results.Ok(form);
}).RequireAuthorization();

app.MapPost("/api/evaluations/{activityId:int}", async (int activityId,
    SaveEvaluationsRequest req, IEvaluationService svc, IResultsService results,
    IEmailService email, ClaimsPrincipal user) =>
{
    if (!user.IsInRole("Student")) return Results.Forbid();
    var ok = await svc.SaveAsync(activityId, GetUserId(user), req, email);
    if (ok) await results.InvalidateCacheAsync(activityId);
    return ok ? Results.NoContent() : Results.BadRequest();
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// RESULTATS
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/results/{activityId:int}", async (int activityId,
    IResultsService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var r = await svc.GetResultsAsync(activityId, GetUserId(user), IsAdmin(user));
    return r is null ? Results.NotFound() : Results.Ok(r);
}).RequireAuthorization();

app.MapGet("/api/results/{activityId:int}/chart", async (int activityId,
    IResultsService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var r = await svc.GetChartAsync(activityId, GetUserId(user), IsAdmin(user));
    return r is null ? Results.NotFound() : Results.Ok(r);
}).RequireAuthorization();

app.MapGet("/api/results/{activityId:int}/csv", async (int activityId,
    IResultsService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var result = await svc.ExportCsvAsync(activityId, GetUserId(user), IsAdmin(user));
    if (result is null) return Results.NotFound();
    var (content, fileName) = result.Value;
    return Results.File(content, "text/csv; charset=utf-8", fileName);
}).RequireAuthorization();

app.MapGet("/api/results/{activityId:int}/excel", async (int activityId,
    IResultsService svc, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var result = await svc.ExportExcelAsync(activityId, GetUserId(user), IsAdmin(user));
    if (result is null) return Results.NotFound();
    var (content, fileName) = result.Value;
    return Results.File(content,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
}).RequireAuthorization();

// ── Criteri ───────────────────────────────────────────────────────────────────
app.MapGet("/api/criteria", () =>
    Results.Ok(Criteria.All.Select(c => new CriteriaDto(c.Key, c.Label))));

// ── Compte d'avaluacions (per confirmació d'eliminació) ────────────────────────
app.MapGet("/api/activities/{id:int}/evals-count", async (
    int id, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var count = await db.Evaluations.CountAsync(e => e.ActivityId == id);
    return Results.Ok(new { count });
}).RequireAuthorization();

// ── Health check ──────────────────────────────────────────────────────────────
app.MapGet("/api/health", async (AppDbContext db, StackExchange.Redis.IConnectionMultiplexer redis) =>
{
    var dbOk    = false;
    var redisOk = false;
    try { dbOk    = await db.Database.CanConnectAsync(); }    catch { }
    try { redisOk = redis.IsConnected; }                      catch { }
    var status = dbOk && redisOk ? "ok" : "degraded";
    return Results.Ok(new { status, db = dbOk ? "ok" : "error", redis = redisOk ? "ok" : "error" });
}).RequireAuthorization();

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

    // Totes les activitats amb el professor propietari (via mòdul)
    var profActivities = await db.Activities
        .Join(db.Modules, a => a.ModuleId, m => m.Id,
              (a, m) => new { ActivityId = a.Id, m.ProfessorId })
        .ToListAsync();

    // Membres per activitat (per calcular participació)
    var memberCounts = await db.GroupMembers
        .GroupBy(gm => gm.Group.ActivityId)
        .Select(g => new { ActivityId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.ActivityId, x => x.Count);

    // Alumnes que han enviat autoavaluació (IsSelf) per activitat
    var submittedCounts = await db.Evaluations
        .Where(e => e.IsSelf)
        .Select(e => new { e.ActivityId, e.EvaluatorId })
        .Distinct()
        .GroupBy(e => e.ActivityId)
        .Select(g => new { ActivityId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.ActivityId, x => x.Count);

    // Accessos per mes (últims 6 mesos) — tipus anònim per evitar problemes de traducció SQL
    var monthlyLogins = (await db.ProfessorLogins
        .Where(l => l.CreatedAt >= since6mo)
        .GroupBy(l => new { l.CreatedAt.Year, l.CreatedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .OrderBy(m => m.Year).ThenBy(m => m.Month)
        .ToListAsync())
        .Select(m => new MonthlyStatDto(m.Year, m.Month, m.Count))
        .ToList();

    // Activitats creades per mes (últims 6 mesos)
    var monthlyActivities = (await db.Activities
        .Where(a => a.CreatedAt >= since6mo)
        .GroupBy(a => new { a.CreatedAt.Year, a.CreatedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .OrderBy(m => m.Year).ThenBy(m => m.Month)
        .ToListAsync())
        .Select(m => new MonthlyStatDto(m.Year, m.Month, m.Count))
        .ToList();

    var stats = professors.Select(p =>
    {
        var myIds = profActivities
            .Where(x => x.ProfessorId == p.Id).Select(x => x.ActivityId).ToList();
        var login = loginStats.FirstOrDefault(l => l.ProfessorId == p.Id);

        var parts = myIds
            .Where(id => memberCounts.ContainsKey(id) && memberCounts[id] > 0)
            .Select(id => Math.Min(100.0, (double)submittedCounts.GetValueOrDefault(id) / memberCounts[id] * 100))
            .ToList();

        return new ProfessorStatsDto(
            p.Id, p.NomComplet, p.Email, p.IsAdmin,
            login?.Last30 ?? 0,
            myIds.Count,
            Math.Round(parts.Count > 0 ? parts.Average() : 0, 1),
            login?.LastAccess);
    }).ToList();

    return Results.Ok(new AdminStatsDto(stats, monthlyLogins, monthlyActivities));
}).RequireAuthorization();

app.MapDelete("/api/admin/stats/logins", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsAdmin(user)) return Results.Forbid();
    await db.ProfessorLogins.ExecuteDeleteAsync();
    return Results.NoContent();
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
    var fileName = $"autoco_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
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
// NOTES DEL PROFESSOR PER ALUMNE
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/notes/{activityId:int}/{studentId:int}", async (
    int activityId, int studentId, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var activity = await db.Activities.Include(a => a.Module)
        .FirstOrDefaultAsync(a => a.Id == activityId);
    if (activity is null) return Results.NotFound();
    if (activity.Module.ProfessorId != GetUserId(user) && !IsAdmin(user)) return Results.Forbid();

    var note = await db.ProfessorNotes
        .FirstOrDefaultAsync(n => n.ActivityId == activityId && n.StudentId == studentId);
    if (note is null) return Results.Ok(new ProfessorNoteDto(studentId, "", DateTime.UtcNow));
    return Results.Ok(new ProfessorNoteDto(note.StudentId, note.Note, note.UpdatedAt));
}).RequireAuthorization();

app.MapGet("/api/notes/{activityId:int}", async (
    int activityId, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var activity = await db.Activities.Include(a => a.Module)
        .FirstOrDefaultAsync(a => a.Id == activityId);
    if (activity is null) return Results.NotFound();
    if (activity.Module.ProfessorId != GetUserId(user) && !IsAdmin(user)) return Results.Forbid();

    var notes = await db.ProfessorNotes
        .Where(n => n.ActivityId == activityId)
        .Select(n => new ProfessorNoteDto(n.StudentId, n.Note, n.UpdatedAt))
        .ToListAsync();
    return Results.Ok(notes);
}).RequireAuthorization();

app.MapPut("/api/notes/{activityId:int}/{studentId:int}", async (
    int activityId, int studentId, SaveNoteRequest req,
    AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var activity = await db.Activities.Include(a => a.Module)
        .FirstOrDefaultAsync(a => a.Id == activityId);
    if (activity is null) return Results.NotFound();
    if (activity.Module.ProfessorId != GetUserId(user) && !IsAdmin(user)) return Results.Forbid();

    var note = await db.ProfessorNotes
        .FirstOrDefaultAsync(n => n.ActivityId == activityId && n.StudentId == studentId);
    if (note is null)
    {
        note = new ProfessorNote
        {
            ActivityId = activityId, StudentId = studentId,
            Note = req.Note.Trim(), UpdatedAt = DateTime.UtcNow
        };
        db.ProfessorNotes.Add(note);
    }
    else
    {
        note.Note = req.Note.Trim();
        note.UpdatedAt = DateTime.UtcNow;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new ProfessorNoteDto(note.StudentId, note.Note, note.UpdatedAt));
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// PLANTILLES D'ACTIVITAT
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/templates", async (AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var professorId = GetUserId(user);
    var isAdm = IsAdmin(user);
    var list = await db.ActivityTemplates
        .Where(t => t.ProfessorId == professorId || isAdm)
        .OrderByDescending(t => t.CreatedAt)
        .ToListAsync();

    // Precarrega noms de professors per a l'admin (evita N+1)
    var professorIds  = list.Select(t => t.ProfessorId).Distinct().ToList();
    var professorNames = await db.Professors
        .Where(p => professorIds.Contains(p.Id))
        .ToDictionaryAsync(p => p.Id, p => p.NomComplet);

    var dtos = list.Select(t =>
    {
        var criteria = System.Text.Json.JsonSerializer.Deserialize<List<CriterionItem>>(t.CriteriaJson)
            ?? new List<CriterionItem>();
        professorNames.TryGetValue(t.ProfessorId, out var profName);
        return new ActivityTemplateDto(t.Id, t.Name, t.Description, criteria, t.CreatedAt, profName);
    }).ToList();
    return Results.Ok(dtos);
}).RequireAuthorization();

app.MapPost("/api/templates", async (CreateTemplateRequest req, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var criteriaJson = System.Text.Json.JsonSerializer.Serialize(req.Criteria ?? new List<CriterionItem>());
    var t = new ActivityTemplate
    {
        ProfessorId  = GetUserId(user),
        Name         = req.Name.Trim(),
        Description  = req.Description?.Trim(),
        CriteriaJson = criteriaJson,
        CreatedAt    = DateTime.UtcNow
    };
    db.ActivityTemplates.Add(t);
    await db.SaveChangesAsync();
    var criteria = System.Text.Json.JsonSerializer.Deserialize<List<CriterionItem>>(t.CriteriaJson)
        ?? new List<CriterionItem>();
    return Results.Created($"/api/templates/{t.Id}",
        new ActivityTemplateDto(t.Id, t.Name, t.Description, criteria, t.CreatedAt));
}).RequireAuthorization();

app.MapDelete("/api/templates/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var t = await db.ActivityTemplates.FindAsync(id);
    if (t is null) return Results.NotFound();
    if (t.ProfessorId != GetUserId(user) && !IsAdmin(user)) return Results.Forbid();
    db.ActivityTemplates.Remove(t);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// ════════════════════════════════════════════════════════════════════════════
// REGISTRE D'ACTIVITAT (LOG)
// ════════════════════════════════════════════════════════════════════════════

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
app.MapPost("/api/examen/checkin", async (CheckinRequest req, IExamenService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Mac))
        return Results.BadRequest(new { error = "Email i MAC són obligatoris." });
    var (resp, error) = await svc.CheckinAsync(req);
    return error is not null
        ? Results.NotFound(new { error })
        : Results.Ok(resp);
}).RequireRateLimiting("auth");

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

// ════════════════════════════════════════════════════════════════════════════
// REGISTRE D'ACTIVITAT (LOG)
// ════════════════════════════════════════════════════════════════════════════

app.MapGet("/api/activities/{id:int}/log", async (
    int id, AppDbContext db, ClaimsPrincipal user) =>
{
    if (!IsProfessor(user)) return Results.Forbid();
    var activity = await db.Activities.Include(a => a.Module)
        .FirstOrDefaultAsync(a => a.Id == id);
    if (activity is null) return Results.NotFound();
    if (activity.Module.ProfessorId != GetUserId(user) && !IsAdmin(user)) return Results.Forbid();

    var logs = await db.ActivityLogs
        .Where(l => l.ActivityId == id)
        .OrderByDescending(l => l.CreatedAt)
        .Take(100)
        .Select(l => new ActivityLogDto(l.Id, l.Action, l.ActorName, l.Details, l.CreatedAt))
        .ToListAsync();
    return Results.Ok(logs);
}).RequireAuthorization();

app.Run();
