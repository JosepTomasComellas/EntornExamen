namespace AutoCo.Shared;

/// <summary>Versió de l'aplicació. Actualitzar en cada canvi significatiu.</summary>
public static class AppVersion
{
    public const string Current   = "1.3.0";
    public const string Name      = "EntornExamen";
    public const string AutoCoBase = "2.2.3";   // Versió base d'AutoCo de la qual és fork

    /// <summary>Descripció del canvi per al changelog intern.</summary>
    public const string ChangeLog = "v1.3.0: importació EPSS (XLS alumnes + ZIP fotos), fix DNI numèric fotos";
}
