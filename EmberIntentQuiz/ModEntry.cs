using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace EmberIntentQuiz;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "MentatUran.EmberIntentQuiz";
    private static Harmony? _harmony;

    public static string ModDirectory { get; private set; } = AppContext.BaseDirectory;

    public static void Initialize()
    {
        ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
        QuestionBank.Load(ModDirectory);

        var combat = CombatManager.Instance;
        combat.CombatSetUp += IntentGate.OnCombatSetUp;
        combat.TurnStarted += IntentGate.OnTurnStarted;
        combat.CombatEnded += IntentGate.OnCombatEnded;

        try
        {
            _harmony = new Harmony(ModId);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Info("[EmberIntentQuiz] Mod initialized.");
        }
        catch (Exception exception)
        {
            Log.Error($"[EmberIntentQuiz] Failed to patch intent visibility: {exception}");
        }
    }
}
