using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Api.Hubs;
using EntornExamen.Api.Services;
using EntornExamen.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace EntornExamen.Tests;

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

    private static ExamenHub CreateHub() =>
        new ExamenHub(null!); // Redis null: ExamenHub el gestiona graciosament en tests

    private static ExamenService CreateSvc(AppDbContext db) =>
        new ExamenService(db, CreateHub(), CreateConfig(),
            NullLogger<ExamenService>.Instance);

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

    // IP de test representativa de la xarxa d'examen
    private const string TestIp  = "192.168.100.101";
    private const string TestIp2 = "192.168.100.102";
    private const string TestMac = "aa:bb:cc:dd:ee:ff";

    // ── Tests de CreateSessio ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateSessio_ClasseExistent_RetornaSessio()
    {
        var (db, profId, classId, _) = SeedBase(nameof(CreateSessio_ClasseExistent_RetornaSessio));
        var svc = CreateSvc(db);

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
        var svc = CreateSvc(db);

        var (sessio, error) = await svc.CreateSessioAsync(
            new CreateSessioRequest(999, "Sense classe", null), profId);

        Assert.NotNull(error);
        Assert.Null(sessio);
    }

    [Fact]
    public async Task CreateSessio_DobleSessioActiva_Conflict()
    {
        var (db, profId, classId, _) = SeedBase(nameof(CreateSessio_DobleSessioActiva_Conflict));
        var svc = CreateSvc(db);

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
        var svc = CreateSvc(db);

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
        var svc = CreateSvc(db);

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan@gmail.com"), TestIp);

        Assert.NotNull(error);
        Assert.Null(resp);
        Assert.Contains("sarria.salesians.cat", error);
    }

    [Fact]
    public async Task Checkin_EmailNoTrobat_RetornaError()
    {
        var (db, _, _, _) = SeedBase(nameof(Checkin_EmailNoTrobat_RetornaError));
        var svc = CreateSvc(db);

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("desconegut@sarria.salesians.cat"), TestIp);

        Assert.NotNull(error);
        Assert.Null(resp);
        Assert.Contains("Email no reconegut", error);
    }

    [Fact]
    public async Task Checkin_SenseSessioActiva_RetornaError()
    {
        var (db, _, _, _) = SeedBase(nameof(Checkin_SenseSessioActiva_RetornaError));
        var svc = CreateSvc(db);

        // No creem cap sessió
        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);

        Assert.NotNull(error);
        Assert.Contains("No hi ha examen actiu", error);
    }

    [Fact]
    public async Task Checkin_CorrecteSessioActiva_CreaSessioConnexio()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(Checkin_CorrecteSessioActiva_CreaSessioConnexio));
        var svc = CreateSvc(db);

        await svc.CreateSessioAsync(new CreateSessioRequest(classId, "Examen T1", null), profId);

        var (resp, error) = await svc.CheckinAsync(
            new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);

        Assert.Null(error);
        Assert.NotNull(resp);
        Assert.Equal("Joan", resp.Alumne.Nom);
        Assert.Equal("Mas", resp.Alumne.Cognoms);
        Assert.Equal("Examen T1", resp.Sessio.Titol);

        // Sense DHCP previ, la IP s'usa com a identificador provisional
        var registre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.StudentId == studentId);
        Assert.NotNull(registre);
        Assert.Equal(EstatConnexio.Connectat, registre.Estat);
        Assert.Equal(TestIp, registre.IpAssignada);
    }

    [Fact]
    public async Task Checkin_DobleCheckin_ActualitzaUltimCheckin()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(Checkin_DobleCheckin_ActualitzaUltimCheckin));
        var svc = CreateSvc(db);

        await svc.CreateSessioAsync(new CreateSessioRequest(classId, "Test", null), profId);

        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);

        var araAbans = DateTime.UtcNow;
        await Task.Delay(50);

        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);

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
        var svc = CreateSvc(db);

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
        var svc = CreateSvc(db);

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, null, null), profId);
        Assert.NotNull(sessio);

        await svc.SetMissatgeAsync(sessio.Id, "Hola", profId, false);
        await svc.SetMissatgeAsync(sessio.Id, null, profId, false);

        var sessioDb = await db.SessionsExamen.FindAsync(sessio.Id);
        Assert.Null(sessioDb?.MissatgeActiu);
    }

    // ── Tests DHCP ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDhcp_Connected_MarcaAlumneConnectat()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(ProcessDhcp_Connected_MarcaAlumneConnectat));
        var svc = CreateSvc(db);

        // Checkin previ crea un registre provisional (IP com a MAC)
        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test DHCP", null), profId);
        Assert.NotNull(sessio);
        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);

        // Simulem desconnexió manual
        var registre = await db.RegistresConnexio.FirstAsync(r => r.StudentId == studentId);
        registre.Estat = EstatConnexio.Desconnectat;
        await db.SaveChangesAsync();

        // Evento DHCP connected: la IP real coincideix amb la provisional → actualitza MAC
        await svc.ProcessDhcpEventAsync(
            new DhcpEventRequest(TestMac, TestIp, "connected"));

        registre = await db.RegistresConnexio.FirstAsync(r => r.StudentId == studentId);
        Assert.Equal(EstatConnexio.Connectat, registre.Estat);
        Assert.Equal(TestMac, registre.MacAddress);
        Assert.Equal(TestIp, registre.IpAssignada);
    }

    [Fact]
    public async Task ProcessDhcp_Disconnected_MarcaAlumneDesconnectat()
    {
        var (db, profId, classId, studentId) =
            SeedBase(nameof(ProcessDhcp_Disconnected_MarcaAlumneDesconnectat));
        var svc = CreateSvc(db);

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test DHCP Disc", null), profId);
        Assert.NotNull(sessio);

        // Checkin + DHCP connected per establir la MAC real
        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);
        await svc.ProcessDhcpEventAsync(new DhcpEventRequest(TestMac, TestIp, "connected"));

        // DHCP disconnected per la MAC real
        await svc.ProcessDhcpEventAsync(
            new DhcpEventRequest(TestMac, null, "disconnected"));

        var registre = await db.RegistresConnexio.FirstAsync(r => r.StudentId == studentId);
        Assert.Equal(EstatConnexio.Desconnectat, registre.Estat);
        Assert.NotNull(registre.DesconnectatAt);
    }

    [Fact]
    public async Task ProcessDhcp_MacDesconeguda_CreaRegistreSenseEstudiant()
    {
        var (db, profId, classId, _) =
            SeedBase(nameof(ProcessDhcp_MacDesconeguda_CreaRegistreSenseEstudiant));
        var svc = CreateSvc(db);

        var (sessio, _) = await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test MAC desc.", null), profId);
        Assert.NotNull(sessio);

        await svc.ProcessDhcpEventAsync(
            new DhcpEventRequest("ff:ff:ff:ff:ff:ff", "192.168.100.200", "connected"));

        var registre = await db.RegistresConnexio
            .FirstOrDefaultAsync(r => r.MacAddress == "ff:ff:ff:ff:ff:ff");
        Assert.NotNull(registre);
        Assert.Null(registre.StudentId);
        Assert.Equal("192.168.100.200", registre.IpAssignada);
    }

    [Fact]
    public async Task ProcessDhcp_SenseSessioActiva_NoFaRes()
    {
        var (db, _, _, _) =
            SeedBase(nameof(ProcessDhcp_SenseSessioActiva_NoFaRes));
        var svc = CreateSvc(db);

        // No hi ha sessió activa
        await svc.ProcessDhcpEventAsync(
            new DhcpEventRequest(TestMac, TestIp, "connected"));

        var count = await db.RegistresConnexio.CountAsync();
        Assert.Equal(0, count);
    }

    // ── Tests GetMacs / DeleteMac ─────────────────────────────────────────────

    [Fact]
    public async Task GetMacs_Admin_RetornaLlista()
    {
        var (db, profId, classId, _) =
            SeedBase(nameof(GetMacs_Admin_RetornaLlista));
        var svc = CreateSvc(db);

        await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test", null), profId);
        // Checkin + DHCP perquè es creï l'AlumneMac amb la MAC real
        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);
        await svc.ProcessDhcpEventAsync(new DhcpEventRequest(TestMac, TestIp, "connected"));

        var macs = await svc.GetMacsAsync(isAdmin: true);
        Assert.Single(macs);
        Assert.Equal(TestMac, macs[0].Mac);
    }

    [Fact]
    public async Task GetMacs_NoAdmin_RetornaLlistaVuida()
    {
        var (db, profId, classId, _) =
            SeedBase(nameof(GetMacs_NoAdmin_RetornaLlistaVuida));
        var svc = CreateSvc(db);

        await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test", null), profId);
        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);
        await svc.ProcessDhcpEventAsync(new DhcpEventRequest(TestMac, TestIp, "connected"));

        var macs = await svc.GetMacsAsync(isAdmin: false);
        Assert.Empty(macs);
    }

    [Fact]
    public async Task DeleteMac_Admin_EliminaRegistre()
    {
        var (db, profId, classId, _) =
            SeedBase(nameof(DeleteMac_Admin_EliminaRegistre));
        var svc = CreateSvc(db);

        await svc.CreateSessioAsync(
            new CreateSessioRequest(classId, "Test", null), profId);
        await svc.CheckinAsync(new CheckinRequest("joan.mas@sarria.salesians.cat"), TestIp);
        await svc.ProcessDhcpEventAsync(new DhcpEventRequest(TestMac, TestIp, "connected"));

        var macId = (await db.AlumneMacs.FirstAsync()).Id;
        var ok    = await svc.DeleteMacAsync(macId, isAdmin: true);

        Assert.True(ok);
        Assert.Equal(0, await db.AlumneMacs.CountAsync());
    }
}
