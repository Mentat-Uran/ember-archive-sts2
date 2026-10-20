using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace EmberIntentQuiz;

internal static class IntentGate
{
    private static readonly HashSet<NIntent> HiddenByMod = [];
    private static bool _active;
    private static bool _hidden;
    private static bool _promptOpen;
    private static bool _emptyBankWarningSent;
    private static int _turnToken;
    private static QuestionOverlay? _overlay;

    public static void OnCombatSetUp(CombatState state)
    {
        CloseOverlay();
        RestoreHiddenIntents();
        _turnToken++;
        _promptOpen = false;
        _emptyBankWarningSent = false;
        _active = state.Players.Count == 1;
        _hidden = _active;
        if (!_active)
            Log.Warn("[EmberIntentQuiz] This mod is limited to single-player combat; intent gating is disabled for multiplayer.");
    }

    public static void OnTurnStarted(CombatState state)
    {
        if (!_active || !CombatManager.Instance.IsPlayPhase) return;

        _turnToken++;
        var token = _turnToken;
        _hidden = true;
        _promptOpen = true;

        var question = QuestionBank.Draw();
        if (question is null)
        {
            _promptOpen = false;
            if (!_emptyBankWarningSent)
            {
                Log.Warn("[EmberIntentQuiz] No question is available. Monster intents remain hidden for this combat.");
                _emptyBankWarningSent = true;
            }
            return;
        }

        Callable.From(() =>
        {
            _ = ShowWhenModalSlotIsFree(question, token, 0);
        }).CallDeferred();
    }

    public static void OnCombatEnded(MegaCrit.Sts2.Core.Rooms.CombatRoom room)
    {
        _turnToken++;
        _active = false;
        _hidden = false;
        _promptOpen = false;
        CloseOverlay();
        RestoreHiddenIntents();
    }

    public static void ApplyVisibility(NIntent intent)
    {
        if (!GodotObject.IsInstanceValid(intent)) return;

        if (_active && _hidden)
        {
            if (intent.Visible)
            {
                HiddenByMod.Add(intent);
                intent.Visible = false;
            }
            return;
        }

        if (HiddenByMod.Remove(intent)) intent.Visible = true;
    }

    private static async Task ShowWhenModalSlotIsFree(QuizQuestion question, int token, int attempt)
    {
        if (!_active || !_promptOpen || token != _turnToken) return;
        var modalContainer = NModalContainer.Instance;
        if (modalContainer is null || !GodotObject.IsInstanceValid(modalContainer))
        {
            _promptOpen = false;
            Log.Warn("[EmberIntentQuiz] The game's modal layer was unavailable; intent remains hidden for this turn.");
            return;
        }

        if (modalContainer.OpenModal is null)
        {
            var overlay = new QuestionOverlay();
            overlay.Configure(question, SetAnswer, () => OnOverlayClosed(overlay));
            modalContainer.Add(overlay);
            if (ReferenceEquals(modalContainer.OpenModal, overlay))
            {
                _overlay = overlay;
                return;
            }
            overlay.Free();
        }

        if (attempt >= 120)
        {
            _promptOpen = false;
            Log.Warn("[EmberIntentQuiz] Another modal stayed open; intent remains hidden for this turn.");
            return;
        }

        try
        {
            var timer = modalContainer.GetTree().CreateTimer(0.25);
            await modalContainer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            await ShowWhenModalSlotIsFree(question, token, attempt + 1);
        }
        catch (Exception exception)
        {
            _promptOpen = false;
            Log.Warn($"[EmberIntentQuiz] Could not open the question panel; intent remains hidden for this turn. {exception.Message}");
        }
    }

    private static void SetAnswer(bool correct)
    {
        if (!_active) return;
        _hidden = !correct;
    }

    private static void OnOverlayClosed(QuestionOverlay overlay)
    {
        if (ReferenceEquals(_overlay, overlay)) _overlay = null;
        _promptOpen = false;
    }

    private static void CloseOverlay()
    {
        var overlay = _overlay;
        _overlay = null;
        if (overlay is null || !GodotObject.IsInstanceValid(overlay)) return;

        var modalContainer = NModalContainer.Instance;
        if (modalContainer is not null && ReferenceEquals(modalContainer.OpenModal, overlay))
            modalContainer.Clear();
        else
            overlay.QueueFree();
    }

    private static void RestoreHiddenIntents()
    {
        foreach (var intent in HiddenByMod)
        {
            if (GodotObject.IsInstanceValid(intent)) intent.Visible = true;
        }
        HiddenByMod.Clear();
    }
}

[HarmonyPatch(typeof(NIntent), nameof(NIntent._Process))]
internal static class IntentVisibilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(NIntent __instance) => IntentGate.ApplyVisibility(__instance);
}
