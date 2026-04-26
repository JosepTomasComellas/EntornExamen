using AutoCo.Api.Data;
using AutoCo.Api.Data.Models;
using AutoCo.Api.Hubs;
using AutoCo.Api.Services;
using AutoCo.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace AutoCo.Tests;

/// <summary>
/// Tests unitaris d'ExamenService.
/// Usa EF Core InMemory i stubs lleugers per a Redis i configuració.
/// </summary>
public class ExamenServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Examen:DominiEmail"] = "sarria.salesians.cat"
            })
            .Build();

    // Stub mínim d'IConnectionMultiplexer per a ExamenHub (no publica res realment)
    private static IConnectionMultiplexer CreateRedisStub()
    {
        // Usem Redis en mode no connectat (ConnectionMultiplexer.ConnectAsync no és factible en tests)
        // Retornem null i EnxamenHub el gestiona graciosament
        return null!;
    }

    private static ExamenHub CreateHub() =>
        new ExamenHub(CreateRedisStub());

    private static (AppDbContext db, int profId, int classId, int studentId)
        SeedBase(string dbName)
    {
        var db = CreateDb(dbName);

        var prof = new Professor
        {
            Id = 1, Email = "prof@test.cat", Nom = "Anna", Cognoms = "Puig",
            PasswordHash = "x", IsAdmin = false
        };
        var cls  = new Class  { Id = 1, Name = "ASIX1" };
        var stud = new Student
        {
            Id = 1, ClassId = 1, Nom = "Joan", Cognoms = "Mas",
            Email = "joan.mas@sarria.salesians.cat",
            NumLlista = 1, PasswordHash = "x"
        };

        db.Professors.Add(prof);
        db.Classes.Add(cls);
        db.Students.Add(stud);
        db.SaveChanges();
        return (db, prof.Id, cls.Id, stud.Id);
    }

    // ── Tests de CreateSessio ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessio_ClasseExistent_RetornaSessio()
    {
        var (db, profId, classId, _) = SeedBase(nameof(CreateSessio_ClasseExistent_RetornaSessio));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (sessio, error) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Prova T1", "Instruccions"), profId);

        Assert.Null(error);
        Assert.NotNull(sessio);
        Assert.Equal("Prova T1", sessio.Titol);
        Assert.True(sessio.Activa);
        Assert.Equal(classId, sessio.ClassId);
    }

    [Fact]
    public async Task CreateSessio_ClasseNoExistent_RetornaError()
    {
        var (db, profId, _, _) = SeedBase(nameof(CreateSessio_ClasseNoExistent_RetornaError));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (sessio, error) = await svc.CreateSessioAsync(
            new CreateSessioRequest(999, "Sense classe", null), profId);

        Assert.NotNull(error);
        Assert.Null(sessio);
    }

    [Fact]
    public async Task CreateSessio_DobleSessioActiva_Conflict()
    {
        var (db, profId, classId, _) = SeedBase(nameof(CreateSessio_DobleSessioActiva_Conflict));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        await svc.CreateSessioAsync(new CreateSessioRequest(classId, "Primera", null), profId);
        var (sessio, error) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Segona", null), profId);

        Assert.NotNull(error);
        Assert.Null(sessio);
        Assert.Contains("activa", error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Tests de TancarSessio ─────────────────────────────────────────────────

    [Fact]
    public async Task TancarSessio_SessioActiva_EsDeveTancada()
    {
        var (db, profId, classId, _) = SeedBase(nameof(TancarSessio_SessioActiva_EsDeveTancada));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test", null), profId);
        Assert.NotNull(sessio);

        var (ok, error) = await svc.TancarSessioAsync(sessio.Id, profId, isAdmin: false);

        Assert.True(ok);
        Assert.Null(error);

        var sessioDb = await db.SessionsExamen.FindAsync(sessio.Id);
        Assert.NotNull(sessioDb);
        Assert.False(sessioDb.Activa);
        Assert.NotNull(sessioDb.TancadaAt);
    }

    // ── Tests de Checkin ──────────────────────────────────────────────────────

    [Fact]
    public async Task Checkin_DominiIncorrecte_RetornaError()
    {
        var (db, _, _, _) = SeedBase(nameof(Checkin_DominiIncorrecte_RetornaError));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan@gmail.com", "aa:bb:cc:dd:ee:ff"));

        Assert.NotNull(error);
        Assert.Null(resp);
        Assert.Contains("sarria.salesians.cat", error);
    }

    [Fact]
    public async Task Checkin_EmailNoTrobat_RetornaError()
    {
        var (db, _, _, _) = SeedBase(nameof(Checkin_EmailNoTrobat_RetornaError));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("desconegut@sarria.salesians.cat", "aa:bb:cc:dd:ee:ff"));

        Assert.NotNull(error);
        Assert.Null(resp);
        Assert.Contains("Email no reconegut", error);
    }

    [Fact]
    public async Task Checkin_SenseSessioActiva_RetornaError()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(Checkin_SenseSessioActiva_RetornaError));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        // No creem cap sessió
        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat", "aa:bb:cc:dd:ee:ff"));

        Assert.NotNull(error);
        Assert.Contains("No hi ha examen actiu", error);
    }

    [Fact]
    public async Task Checkin_CorrecteSessioActiva_CreaSessioConnexio()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(Checkin_CorrecteSessioActiva_CreaSessioConnexio));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        await svc.CreateSessioAsync(new CreateSessioRequest(classId, "Examen T1", null), profId);

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat", "aa:bb:cc:dd:ee:ff"));

        Assert.Null(error);
        Assert.NotNull(resp);
        Assert.Equal("Joan", resp.Alumne.Nom);
        Assert.Equal("Mas", resp.Alumne.Cognoms);
        Assert.Equal("Examen T1", resp.Sessio.Titol);

        // Comprova que s'ha creat el registre
        var registre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.StudentId == studentId);
        Assert.NotNull(registre);
        Assert.Equal(EstatConnexio.Connectat, registre.Estat);
        Assert.Equal("aa:bb:cc:dd:ee:ff", registre.MacAddress);
    }

    [Fact]
    public async Task Checkin_DobleCheckin_ActualitzaUltimCheckin()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(Checkin_DobleCheckin_ActualitzaUltimCheckin));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        await svc.CreateSessioAsync(new CreateSessioRequest(classId, "Test", null), profId);

        await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat", "aa:bb:cc:dd:ee:ff"));

        var araAbans = DateTime.UtcNow;
        await Task.Delay(50);

        await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat", "aa:bb:cc:dd:ee:ff"));

        var registre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.StudentId == studentId);
        Assert.NotNull(registre?.UltimCheckinAt);
        Assert.True(registre.UltimCheckinAt > araAbans);

        // No s'ha de duplicar el registre
        var count = await db.RegistresConnexio.CountAsync(r => r.StudentId == studentId);
        Assert.Equal(1, count);
    }

    // ── Tests de SetMissatge ──────────────────────────────────────────────────

    [Fact]
    public async Task SetMissatge_SessioExistent_DesaMissatge()
    {
        var (db, profId, classId, _) = SeedBase(nameof(SetMissatge_SessioExistent_DesaMissatge));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, null, null), profId);
        Assert.NotNull(sessio);

        var (ok, error) = await svc.SetMissatgeAsync(sessio.Id, "Teniu 10 minuts", profId, false);

        Assert.True(ok);
        Assert.Null(error);

        var sessioDb = await db.SessionsExamen.FindAsync(sessio.Id);
        Assert.Equal("Teniu 10 minuts", sessioDb?.MissatgeActiu);
    }

    [Fact]
    public async Task SetMissatge_Null_EsborraMissatge()
    {
        var (db, profId, classId, _) = SeedBase(nameof(SetMissatge_Null_EsborraMissatge));
        var svc = new ExamenService(db, CreateHub(), CreateConfig());

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, null, null), profId);
        Assert.NotNull(sessio);

        await svc.SetMissatgeAsync(sessio.Id, "Hola", profId, false);
        await svc.SetMissatgeAsync(sessio.Id, null, profId, false);

        var sessioDb = await db.SessionsExamen.FindAsync(sessio.Id);
        Assert.Null(sessioDb?.MissatgeActiu);
    }
}
