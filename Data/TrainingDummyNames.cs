namespace AionSniffer.Data;

/// <summary>
/// "Training Dummy" in all 8 languages Chat.log can appear in - verified from the client's own
/// client_strings_npc.xml (key STR_OBJ_TRAINING_DUMMY) in each L10N/&lt;lang&gt;/Data/data.pak, the
/// same client-string extraction method used for skills/places elsewhere in this repo (see
/// assets/README.md), not guessed or machine-translated.
///
/// Practice dummies stand in town squares and get hit by every player who wanders past over an
/// entire Chat.log session, not just one group - unlike a real boss (capped by group/alliance
/// size), a dummy's participant list has no natural bound and reliably blows past the backend's
/// 24-entry cap (uploadSchema.ts), which is exactly the 400 a real upload hit. Per the user,
/// practice dummies were never meant to be tracked as encounters in the first place, so
/// BuildEncounterUpload refuses them outright rather than truncating or filtering the roster.
/// </summary>
public static class TrainingDummyNames
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "Training Dummy", // en
        "Übungsziel", // de
        "Mannequin d'entraînement", // fr
        "Maniquí de entrenamiento", // es
        "Тренировочная кукла", // ru
        "Cel Ćwiczebny", // pl
        "Talim Hedefi", // tr
        "训练用稻草人", // zh
    };

    public static bool IsTrainingDummy(string name) => Names.Contains(name);
}
