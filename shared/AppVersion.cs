namespace AutoCo.Shared;

/// <summary>Versió de l'aplicació. Actualitzar en cada canvi significatiu.</summary>
public static class AppVersion
{
    public const string Current   = "1.3.1";
    public const string Name      = "EntornExamen";
    public const string AutoCoBase = "2.2.3";   // Versió base d'AutoCo de la qual és fork

    /// <summary>Descripció del canvi per al changelog intern.</summary>
    public const string ChangeLog = "v1.3.1: fix ImportarAlumnesXls (suport HTML+XLS), fix Regex LINQ fotos, pas a pas fotos, botó EPSS a Gestió de Classes";
}
