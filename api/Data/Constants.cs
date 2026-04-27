namespace EntornExamen.Api.Data;

/// <summary>Criteris d'avaluació fixes per a totes les activitats.</summary>
public static class Criteria
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("probitat",        "Probitat"),
        ("autonomia",       "Autonomia"),
        ("responsabilitat", "Responsabilitat i Treball de qualitat"),
        ("collaboracio",    "Col·laboració i treball en equip"),
        ("comunicacio",     "Comunicació")
    ];

    public static readonly IReadOnlyList<string> Keys = All.Select(c => c.Key).ToList();
}
