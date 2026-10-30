namespace AionDPS.History;

/// <summary>
/// Which targets are training dummies: the runs of the dummy test only compare with each other when they were fought at one. The names are the client's own for the
/// "Punching Bag" (2000000, 2090438 ...) and the "Training Scarecrow" families (2300229 ..., "Melee Training Scarecrow" 2400035) in every language of
/// assets/aion2/npcs/npc_names.json; the match is a case-insensitive "contains", since a tribe suffix ("Punching Bag_Elyos Tribe") or "(Specimen)" is still a dummy.
/// The list and the class are Stacy's, from her fork (StacysView/Aion-DPS-Meter, MIT); the one place to add a name.
/// </summary>
public static class DummyTargets
{
    private static readonly string[] Names =
    {
        // en
        "Punching Bag", "Training Scarecrow",
        // de
        "Sandsack", "Trainingspuppe",
        // fr
        "Mannequin", "Épouvantail d'entraînement",
        // es
        "Saco de arena", "Muñeco de entrenamiento",
        // ru
        "Мешок с песком", "Тренировочное чучело",
        // pt
        "Saco de pancadas", "Espantalho de Treino", "Espantalho de Treinamento",
        // ja, ko
        "サンドバッグ", "訓練用カカシ", "샌드백", "훈련용 허수아비",
    };

    public static bool IsDummy(string? targetName) =>
        !string.IsNullOrWhiteSpace(targetName)
        && Names.Any(n => targetName.Contains(n, StringComparison.OrdinalIgnoreCase));
}
