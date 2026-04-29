using EntornExamen.Api.Data;
using EntornExamen.Api.Data.Models;
using EntornExamen.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntornExamen.Tests;

public class BindServiceTests : IDisposable
{
    private readonly string _tmpDir;

    public BindServiceTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"bind-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private static AppDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private BindService CreateSvc(AppDbContext db)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetControl:Path"]       = _tmpDir,
                ["NetControl:RedirectIp"] = "192.168.100.1"
            })
            .Build();
        return new BindService(db, cfg, NullLogger<BindService>.Instance);
    }

    // ── Tests CRUD dominis ────────────────────────────────────────────────────

    [Fact]
    public async Task AfegirDomini_DominiNou_EsDesat()
    {
        var db  = CreateDb(nameof(AfegirDomini_DominiNou_EsDesat));
        var svc = CreateSvc(db);

        var dto = await svc.AfegirDominiAsync("facebook.com", "xarxes");

        Assert.NotNull(dto);
        Assert.Equal("facebook.com", dto.Domini);
        Assert.True(dto.Actiu);
        Assert.Equal(1, await db.DominisBlocats.CountAsync());
    }

    [Fact]
    public async Task AfegirDomini_DominiDuplicat_RetornaNull()
    {
        var db  = CreateDb(nameof(AfegirDomini_DominiDuplicat_RetornaNull));
        var svc = CreateSvc(db);

        await svc.AfegirDominiAsync("facebook.com", null);
        var dto = await svc.AfegirDominiAsync("facebook.com", null);

        Assert.Null(dto);
        Assert.Equal(1, await db.DominisBlocats.CountAsync());
    }

    [Fact]
    public async Task AfegirDomini_WildcardPrefix_NormalitzaCorrecte()
    {
        var db  = CreateDb(nameof(AfegirDomini_WildcardPrefix_NormalitzaCorrecte));
        var svc = CreateSvc(db);

        var dto = await svc.AfegirDominiAsync("*.instagram.com", null);

        Assert.NotNull(dto);
        Assert.Equal("instagram.com", dto.Domini);
    }

    [Fact]
    public async Task EliminarDomini_ExistentId_EliminaCorrecte()
    {
        var db  = CreateDb(nameof(EliminarDomini_ExistentId_EliminaCorrecte));
        var svc = CreateSvc(db);

        var dto = await svc.AfegirDominiAsync("tiktok.com", null);
        Assert.NotNull(dto);

        var ok = await svc.EliminarDominiAsync(dto.Id);

        Assert.True(ok);
        Assert.Equal(0, await db.DominisBlocats.CountAsync());
    }

    [Fact]
    public async Task EliminarDomini_IdNoExistent_RetornaFals()
    {
        var db  = CreateDb(nameof(EliminarDomini_IdNoExistent_RetornaFals));
        var svc = CreateSvc(db);

        var ok = await svc.EliminarDominiAsync(999);

        Assert.False(ok);
    }

    [Fact]
    public async Task ToggleActiu_DominiActiu_DesactivaIPosteriorActivaDeNou()
    {
        var db  = CreateDb(nameof(ToggleActiu_DominiActiu_DesactivaIPosteriorActivaDeNou));
        var svc = CreateSvc(db);

        var dto = await svc.AfegirDominiAsync("snapchat.com", null);
        Assert.NotNull(dto);
        Assert.True(dto.Actiu);

        await svc.ToggleActiuAsync(dto.Id);
        var ent = await db.DominisBlocats.FindAsync(dto.Id);
        Assert.False(ent?.Actiu);

        await svc.ToggleActiuAsync(dto.Id);
        ent = await db.DominisBlocats.FindAsync(dto.Id);
        Assert.True(ent?.Actiu);
    }

    // ── Tests generació de fitxers ────────────────────────────────────────────

    [Fact]
    public async Task AplicarCanavis_SenseDominis_GeneraFitxersArrels()
    {
        var db  = CreateDb(nameof(AplicarCanavis_SenseDominis_GeneraFitxersArrels));
        var svc = CreateSvc(db);

        var (ok, error) = await svc.AplicarCanavisAsync();

        Assert.True(ok);
        Assert.Null(error);
        Assert.True(File.Exists(Path.Combine(_tmpDir, "blocked-zones.conf")));
        Assert.True(File.Exists(Path.Combine(_tmpDir, "blocked-zone.db")));
        Assert.True(File.Exists(Path.Combine(_tmpDir, "reload-trigger")));
    }

    [Fact]
    public async Task AplicarCanavis_AmbDominis_ZonesConf_ContéDeclaracions()
    {
        var db  = CreateDb(nameof(AplicarCanavis_AmbDominis_ZonesConf_ContéDeclaracions));
        var svc = CreateSvc(db);

        await svc.AfegirDominiAsync("youtube.com", null);
        await svc.AfegirDominiAsync("twitch.tv", null);

        var (ok, _) = await svc.AplicarCanavisAsync();
        Assert.True(ok);

        var conf = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "blocked-zones.conf"));
        Assert.Contains("zone \"youtube.com\"", conf);
        Assert.Contains("zone \"twitch.tv\"", conf);
        Assert.Contains("blocked-zone.db", conf);
    }

    [Fact]
    public async Task AplicarCanavis_DominiInactiu_NoApareixAlConf()
    {
        var db  = CreateDb(nameof(AplicarCanavis_DominiInactiu_NoApareixAlConf));
        var svc = CreateSvc(db);

        var dto = await svc.AfegirDominiAsync("discord.com", null);
        Assert.NotNull(dto);
        await svc.ToggleActiuAsync(dto.Id); // desactiva

        var (ok, _) = await svc.AplicarCanavisAsync();
        Assert.True(ok);

        var conf = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "blocked-zones.conf"));
        Assert.DoesNotContain("discord.com", conf);
    }

    [Fact]
    public async Task AplicarCanavis_ZoneDb_ContéIpServidor()
    {
        var db  = CreateDb(nameof(AplicarCanavis_ZoneDb_ContéIpServidor));
        var svc = CreateSvc(db);

        await svc.AplicarCanavisAsync();

        var zoneDb = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "blocked-zone.db"));
        Assert.Contains("192.168.100.1", zoneDb);
    }

    [Fact]
    public async Task SetDnsIntercept_Actiu_EscriuFitxer1()
    {
        var db  = CreateDb(nameof(SetDnsIntercept_Actiu_EscriuFitxer1));
        var svc = CreateSvc(db);

        var (ok, _) = await svc.SetDnsInterceptAsync(true);
        Assert.True(ok);

        var contingut = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "dns-intercept"));
        Assert.Equal("1", contingut.Trim());
    }

    [Fact]
    public async Task SetDnsIntercept_Inactiu_EscriuFitxer0()
    {
        var db  = CreateDb(nameof(SetDnsIntercept_Inactiu_EscriuFitxer0));
        var svc = CreateSvc(db);

        await svc.SetDnsInterceptAsync(true);
        var (ok, _) = await svc.SetDnsInterceptAsync(false);
        Assert.True(ok);

        var contingut = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "dns-intercept"));
        Assert.Equal("0", contingut.Trim());
    }

    [Fact]
    public async Task GetStatus_AmbFitxersTrigger_RetornaDataCorrecte()
    {
        var db  = CreateDb(nameof(GetStatus_AmbFitxersTrigger_RetornaDataCorrecte));
        var svc = CreateSvc(db);

        await svc.AplicarCanavisAsync();

        var status = await svc.GetStatusAsync();

        Assert.True(status.BindDisponible);
        Assert.NotNull(status.UltimaAplicacio);
        Assert.Equal(0, status.DominisBlocats);
    }
}
