namespace AutoCo.Shared;

/// <summary>Versió de l'aplicació. Actualitzar en cada canvi significatiu.</summary>
public static class AppVersion
{
    public const string Current   = "1.1.0";
    public const string Name      = "EntornExamen";
    public const string AutoCoBase = "2.2.3";   // Versió base d'AutoCo de la qual és fork

    /// <summary>Descripció del canvi per al changelog intern.</summary>
    public const string ChangeLog = "v1.1.0: i18n examen+portal, filtre plafó, foto drawer, gestió MACs, botó reobrir, fix FotoUrl+timer+stale, tests DHCP+MACs";
}
