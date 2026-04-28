using System.Text.Json.Nodes;
using System.Text.Json;

namespace EntornExamen.Web.Services;

public class BrandConfig(IConfiguration config)
{
    public string Nom          => config["Brand:Nom"]          ?? "Entorn d'Examens";
    public string ShortNom     => config["Brand:ShortNom"]     ?? Nom.Split(' ')[0];
    public string Organitzacio => config["Brand:Organitzacio"] ?? "Salesians de Sarrià · Dept. d'Informàtica";
    public string ColorPrimary => config["Brand:ColorPrimary"] ?? "#CC0000";
    public string ColorAppBar  => config["Brand:ColorAppBar"]  ?? "#1e293b";
    public string LogoUrl      => config["Brand:LogoUrl"]      ?? "/images/logo2.png";
    public string BgImageUrl   => config["Brand:BgImageUrl"]   ?? "/images/fons-salesians.png";

    public string ColorPrimaryDark => DarkenHex(ColorPrimary, 0.15);
    public string ColorAppBarDark  => DarkenHex(ColorAppBar,  0.15);

    public string BgImageCssValue => string.IsNullOrWhiteSpace(BgImageUrl)
        ? "none"
        : $"url('{BgImageUrl}')";

    private static string DarkenHex(string hex, double amount)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return "#" + hex;
            var r = Math.Clamp((int)(Convert.ToInt32(hex[..2], 16) * (1 - amount)), 0, 255);
            var g = Math.Clamp((int)(Convert.ToInt32(hex[2..4], 16) * (1 - amount)), 0, 255);
            var b = Math.Clamp((int)(Convert.ToInt32(hex[4..6], 16) * (1 - amount)), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        catch { return "#" + hex; }
    }

    public string GenerateManifestJson()
    {
        var obj = new JsonObject
        {
            ["name"]             = Nom,
            ["short_name"]       = ShortNom,
            ["description"]      = $"Control de presència en temps real durant exàmens — {Organitzacio}",
            ["start_url"]        = "/",
            ["display"]          = "standalone",
            ["background_color"] = "#ffffff",
            ["theme_color"]      = ColorAppBar,
            ["lang"]             = "ca",
            ["icons"]            = new JsonArray
            {
                new JsonObject { ["src"] = LogoUrl, ["sizes"] = "192x192", ["type"] = "image/png", ["purpose"] = "any maskable" },
                new JsonObject { ["src"] = LogoUrl, ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "any maskable" }
            },
            ["categories"]  = new JsonArray("education", "productivity"),
            ["orientation"] = "any"
        };
        return obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public string GenerateOfflineHtml() => OfflineTemplate
        .Replace("__NOM__",        System.Net.WebUtility.HtmlEncode(Nom))
        .Replace("__PRIMARY__",    ColorPrimary)
        .Replace("__PRIMARY_DK__", ColorPrimaryDark)
        .Replace("__APPBAR__",     ColorAppBar);

    private const string OfflineTemplate =
        "<!DOCTYPE html>\n" +
        "<html lang=\"ca\">\n" +
        "<head>\n" +
        "    <meta charset=\"utf-8\" />\n" +
        "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n" +
        "    <title>__NOM__ — Sense connexió</title>\n" +
        "    <style>\n" +
        "        * { box-sizing: border-box; margin: 0; padding: 0; }\n" +
        "        body { font-family: Roboto, Arial, sans-serif; background: #f5f5f5; display: flex; align-items: center; justify-content: center; min-height: 100vh; color: #333; }\n" +
        "        .card { background: white; border-radius: 12px; padding: 48px 40px; max-width: 420px; width: 90%; text-align: center; box-shadow: 0 4px 20px rgba(0,0,0,0.1); }\n" +
        "        .icon { font-size: 64px; margin-bottom: 24px; }\n" +
        "        h1 { font-size: 1.5rem; font-weight: 700; margin-bottom: 12px; color: __APPBAR__; }\n" +
        "        p { color: #666; line-height: 1.6; margin-bottom: 28px; }\n" +
        "        button { background: __PRIMARY__; color: white; border: none; border-radius: 6px; padding: 12px 28px; font-size: 1rem; cursor: pointer; transition: background 0.2s; }\n" +
        "        button:hover { background: __PRIMARY_DK__; }\n" +
        "    </style>\n" +
        "</head>\n" +
        "<body>\n" +
        "    <div class=\"card\">\n" +
        "        <div class=\"icon\">📡</div>\n" +
        "        <h1>Sense connexió</h1>\n" +
        "        <p>__NOM__ necessita connexió al servidor per funcionar.<br />Comprova la connexió a Internet i torna-ho a provar.</p>\n" +
        "        <button onclick=\"window.location.reload()\">Tornar a intentar</button>\n" +
        "    </div>\n" +
        "</body>\n" +
        "</html>";
}
