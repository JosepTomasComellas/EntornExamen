using EntornExamen.Web;
using EntornExamen.Web.Services;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using StackExchange.Redis;
using EntornExamen.Web.Resources;

// Necessari per a ExcelDataReader: suport d'encodings Windows (cp1252, etc.)
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// L'antiforgery registra com a Error quan troba una cookie vella (clau caducada/canviada),
// però ho gestiona internament emetent una nova cookie. Silenciem el log per evitar
// alarmar en desplegaments normals.
builder.Logging.AddFilter("Microsoft.AspNetCore.Antiforgery", LogLevel.Critical);

var redisConn = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
var redis = await ConnectionMultiplexer.ConnectAsync(redisConn);
builder.Services.AddSingleton<IConnectionMultiplexer>(redis);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Temps màxim per reconnectar-se; passat aquest temps el circuit es destrueix
        // i Dispose() de Portal.razor para el timer de check-in.
        // El CircuitHandler (15 s) actua abans d'aquest timeout.
        options.DisconnectedCircuitMaxRetained = 100;
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(40);
    });

builder.Services.AddSignalR()
    .AddStackExchangeRedis(redisConn, opts =>
        opts.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("EntornExamen"));

builder.Services.AddMudServices();

// ── Localització (i18n) ───────────────────────────────────────────────────────
builder.Services.AddLocalization(opts => opts.ResourcesPath = "Resources");

// Usem DictionaryLocalizer (diccionaris estàtics) en lloc de ResourceManager/resx
// per evitar problemes de resolució de recursos embeguts en Docker.
builder.Services.AddSingleton<IStringLocalizer<SharedResources>, EntornExamen.Web.Resources.DictionaryLocalizer>();

var supportedCultures = new[] { "ca", "es" };
builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(opts =>
{
    opts.SetDefaultCulture("ca");
    opts.AddSupportedCultures(supportedCultures);
    opts.AddSupportedUICultures(supportedCultures);
    // Prioritat: cookie → accept-language header → default (ca)
    opts.ApplyCurrentCultureToResponseHeaders = true;
});

// Persistir les claus de DataProtection al sistema de fitxers (volum Docker independent de Redis)
// Això evita que les claus es perdin si Redis reinicia, i manté la coherència
// de cookies d'antiforgery i sessions de ProtectedLocalStorage entre desplegaments.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo("/app/dp-keys"))
    .SetApplicationName("EntornExamen");

// Configuració de marca (logo, colors, nom del centre)
builder.Services.AddSingleton<BrandConfig>();

// Estat de l'usuari (substitueix ISession + SessionHelper)
builder.Services.AddScoped<UserStateService>();

// ── Entorn Examen: gestió de circuit Blazor (detecta tancament de navegador) ──
builder.Services.AddScoped<ExamenCircuitState>();
builder.Services.AddScoped<CircuitHandler, ExamenCircuitHandler>();

// ── Entorn Examen: notificacions temps real ────────────────────────────────────
builder.Services.AddSingleton<ExamenNotificationService>();
builder.Services.AddHostedService<ExamenRedisSubscriber>();

// HTTP client cap a l'API
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = builder.Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:7000";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout     = TimeSpan.FromMinutes(3); // permet importacions CSV grans i enviaments massius
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error");

app.UseStaticFiles();
app.UseRequestLocalization();
app.UseAntiforgery();

// manifest.json i offline.html es generen dinàmicament per reflectir la marca configurada
app.MapGet("/manifest.json", (BrandConfig brand) =>
    Results.Content(brand.GenerateManifestJson(), "application/manifest+json; charset=utf-8"));

app.MapGet("/offline.html", (BrandConfig brand) =>
    Results.Content(brand.GenerateOfflineHtml(), "text/html; charset=utf-8"));

app.MapRazorComponents<EntornExamen.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
