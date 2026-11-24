using UnityEngine;

namespace Before1800.PartB
{
    public sealed class GreyboxHUD : MonoBehaviour
    {
        private static readonly Color Surface = new Color(0.055f, 0.085f, 0.10f, 0.96f);
        private static readonly Color Ink = new Color(0.95f, 0.97f, 0.95f);
        private static readonly Color Muted = new Color(0.73f, 0.80f, 0.80f);
        private static readonly Color Mint = new Color(0.51f, 0.90f, 0.76f);
        private static readonly Color Amber = new Color(1f, 0.79f, 0.46f);
        private PartBScene scene;
        private PlayerUIView playerView;
        private bool framing;
        private Quaternion previousLook, dialogueLook;
        private float previousFov, dialogueFov, blend;
        public PlayerInteraction Flow { get; private set; }
        private GUIStyle body, small, heading, clock, button;

        public void Bind(PartBScene owner)
        {
            // Restart has already reset the camera pose to its spawn anchor.
            if (framing) owner.World.ViewCamera.fieldOfView = previousFov;
            framing = false;
            scene = owner;
            Flow = new PlayerInteraction(scene);
            if (!playerView) playerView = gameObject.AddComponent<PlayerUIView>();
            playerView.Bind(scene, Flow);
            playerView.SetVisible(true);
        }

        // Called by the scene before the simulation step so opening a window freezes that frame.
        public void ReadShortcuts()
        {
            if (scene == null || scene.State == null || Flow == null) return;
            Flow.Tick();
            ReadCurrentInput();
            if (framing && !ShouldFrameConversation) RestoreLook();
        }

        private void ReadCurrentInput()
        {
            if(playerView.SettingsOpen){if(Input.GetKeyDown(KeyCode.Escape))playerView.CloseSettings();return;}
            if (playerView.Editing)
            {
                if (Input.GetKeyDown(KeyCode.Escape) && string.IsNullOrEmpty(Input.compositionString))
                    playerView.BlurInput();
                return;
            }
            if (Application.isEditor && Input.GetKeyDown(KeyCode.F1) && (Flow.DebugOpen ||
                Flow.Screen == PlayerScreen.Explore || Flow.Screen == PlayerScreen.Talk ||
                Flow.Screen == PlayerScreen.Intro || Flow.Screen == PlayerScreen.Timeout))
            {
                Flow.DebugOpen = !Flow.DebugOpen;
                playerView.SetVisible(!Flow.DebugOpen);
                return;
            }
            if (!Flow.DebugOpen)
            {
                switch (Flow.Screen)
                {
                    case PlayerScreen.Explore:
                        if (Input.GetKeyDown(KeyCode.Escape)) Flow.ToggleMenu();
                        else if (Input.GetKeyDown(KeyCode.J)) Flow.ToggleJournal();
                        else if (Input.GetKeyDown(KeyCode.H)) scene.Story?.OpenHelp();
                        else if (Input.GetKeyDown(KeyCode.E)) Flow.Use();
                        break;
                    case PlayerScreen.Talk:
                        if (Input.GetKeyDown(KeyCode.Escape)) Flow.Back();
                        else if (Input.GetKeyDown(KeyCode.J)) Flow.ToggleJournal();
                        break;
                    case PlayerScreen.Reading:
                    case PlayerScreen.Journal:
                        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.J)) Flow.Back();
                        break;
                    case PlayerScreen.Menu:
                    case PlayerScreen.Wait:
                    case PlayerScreen.Error:
                        if (Input.GetKeyDown(KeyCode.Escape)) Flow.Back();
                        break;
                }
                // The selected uGUI button owns Submit (Enter/Space). The focused text field owns typing.
                return;
            }
            ReadDeveloperShortcuts();
        }

        private bool ShouldFrameConversation => Flow != null && !Flow.DebugOpen && Flow.FramingConversation && scene.DialogueOpen;

        private void LateUpdate()
        {
            if (!ShouldFrameConversation)
            {
                RestoreLook();
                return;
            }
            var camera = scene.World.ViewCamera;
            if (!scene.MenuOpen) scene.World.FaceConversationPartner(Time.unscaledDeltaTime);
            if (!framing)
            {
                framing = true;
                previousLook = camera.transform.localRotation;
                previousFov = camera.fieldOfView;
                var eyeLine = scene.World.Lin.position + Vector3.up * 1.52f - camera.transform.position;
                // Keep the eyes in the upper third, leaving the lower part for subtitles.
                dialogueFov = Mathf.Clamp(2f * Mathf.Atan2(.77f, eyeLine.magnitude) * Mathf.Rad2Deg, 37f, previousFov);
                float down = Mathf.Atan(.31f * Mathf.Tan(dialogueFov * Mathf.Deg2Rad * .5f)) * Mathf.Rad2Deg;
                var worldLook = Quaternion.LookRotation(eyeLine, Vector3.up) * Quaternion.Euler(down, 0f, 0f);
                dialogueLook = Quaternion.Inverse(camera.transform.parent.rotation) * worldLook;
                blend = 0f;
            }
            blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / .18f);
            float t = Mathf.SmoothStep(0f, 1f, blend);
            camera.transform.localRotation = Quaternion.Slerp(previousLook, dialogueLook, t);
            camera.fieldOfView = Mathf.Lerp(previousFov, dialogueFov, t);
        }

        private void RestoreLook()
        {
            if (!framing) return;
            framing = false;
            if (!scene || !scene.World || !scene.World.ViewCamera) return;
            scene.World.ViewCamera.transform.localRotation = previousLook;
            scene.World.ViewCamera.fieldOfView = previousFov;
        }

        private void OnDisable() => RestoreLook();

        private void ReadDeveloperShortcuts()
        {
            if (scene.State.TimedOut)
            {
                if (Input.GetKeyDown(KeyCode.R)) scene.Restart();
                return;
            }
            if (scene.IntroOpen)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    scene.ToggleIntroOff();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) scene.ToggleMenu();
            if (Input.GetKeyDown(KeyCode.E)) scene.ToggleDialogue();
            if (Input.GetKeyDown(KeyCode.M)) scene.ToggleModelWait();
            if (scene.MenuOpen || scene.ModelWait || scene.State.HasPause(PauseReason.Error)) return;
            if (scene.DialogueOpen)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) scene.SubmitTestAction("follow");
                if (Input.GetKeyDown(KeyCode.Alpha2)) scene.SubmitTestAction("stop");
                if (Input.GetKeyDown(KeyCode.Alpha3)) scene.SubmitTestAction("inspect");
                if (Input.GetKeyDown(KeyCode.Alpha4)) scene.SubmitTestAction("withdraw");
                if (Input.GetKeyDown(KeyCode.Alpha5)) scene.SubmitTestAction("restore");
                if (Input.GetKeyDown(KeyCode.Alpha6) && scene.World.Station) scene.SubmitTestAction("inspect_set_back");
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.F)) scene.InteractWithSetBack();
                if (Input.GetKeyDown(KeyCode.V)) scene.TestCompleteCamera();
            }
        }

        private void OnGUI()
        {
            if (scene == null || scene.State == null || Flow == null || !Flow.DebugOpen) return;
            if (body == null) CreateStyles();
            float scale = Mathf.Clamp(Mathf.Min(Screen.height / 520f, Screen.width / 690f), 0.66f, 1.4f);
            scale = Mathf.Min(scale, Screen.height / (scene.MenuOpen && scene.State.CanRetryReturn ? 356f : 322f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                bool windowOpen = scene.State.IsPaused || scene.State.TimedOut;
                bool sideBySide = width >= 675f;
                if (windowOpen) Fill(new Rect(0, 0, width, height), new Color(0f, 0.02f, 0.03f, 0.28f));
                if (!windowOpen || sideBySide) DrawMonitor(height);
                if (windowOpen) DrawWindow(width, height, sideBySide);
                else
                {
                    Fill(new Rect(width / 2f - 2f, height / 2f - 2f, 4f, 4f), Ink);
                    DrawStatusStrip(width, height, true);
                }
                if (windowOpen && height >= 405f) DrawStatusStrip(width, height, false);
            }
            finally { GUI.matrix = previous; }
        }

        private void CreateStyles()
        {
            body = LabelStyle(15, Ink);
            body.wordWrap = true;
            small = LabelStyle(13, Muted);
            heading = LabelStyle(18, Ink, FontStyle.Bold);
            clock = LabelStyle(28, Mint, FontStyle.Bold);
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        }

        private static GUIStyle LabelStyle(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size, fontStyle = weight, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(), clipping = TextClipping.Clip, wordWrap = false
            };
            style.normal.textColor = color;
            return style;
        }

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        // A fixed-width column keeps controls stable across IMGUI Layout, mouse and Repaint events.
        private struct Card
        {
            private readonly float x, width;
            private float y;

            public Card(float left, float top, float cardWidth, float height, Color accent)
            {
                Fill(new Rect(left, top, cardWidth, height), Surface);
                Fill(new Rect(left, top, cardWidth, 3), accent);
                x = left + 14;
                y = top + 11;
                width = cardWidth - 28;
            }

            public Rect Take(float h)
            {
                Rect rect = new Rect(x, y, width, h);
                y += h;
                return rect;
            }
        }

        private void DrawMonitor(float height)
        {
            SessionState state = scene.State;
            bool expanded = height >= 330f;
            var card = new Card(12, 12, 276, expanded ? (scene.World.Station ? 313 : 293) : 70, Mint);
            Rect top = card.Take(19);
            GUI.Label(top, "DEVELOPER MONITOR     [F1]", small);
            Rect time = card.Take(32);
            GUI.Label(time, state.TimeHms, clock);
            string status = state.TimedOut ? "TIMEOUT" : state.IsPaused ? "PAUSED" : "LIVE";
            GUI.Label(new Rect(time.x + 164, time.y, 80, time.height), status, small);
            if (!expanded) return;
            GUI.Label(card.Take(19), "Action  /  " + state.Stage, body);
            GUI.Label(card.Take(21), "Active    " + Describe(state.Active), small);
            GUI.Label(card.Take(21), "Queued  " + Describe(state.Pending), small);
            Divider(ref card);
            string[] evidence = state.VerifiedEvidence;
            GUI.Label(card.Take(23), "Verified   " + evidence.Length +
                (evidence.Length == 0 ? "   (none)" : "   " + string.Join(", ", evidence)), body);
            Concern(ref card, "C1", "Controlled world");
            Concern(ref card, "C2", "Concealment & trust");
            Concern(ref card, "C3", "Choice to leave");
            Divider(ref card);
            Pair(ref card, "Cooperation", state.Cooperation.ToString());
            if (scene.World.Station) Pair(ref card, "Passage checked", state.RouteConfirmed ? "yes" : "no");
            Pair(ref card, "Agreed to escape", state.EscapeAgreed ? "yes" : "no");
        }

        private static string Describe(ActionTicket action)
        {
            if (action == null) return "-";
            return string.IsNullOrEmpty(action.TargetId) ? action.Action : action.Action + " / " + action.TargetId;
        }

        private static void Divider(ref Card card)
        {
            Rect row = card.Take(8);
            Fill(new Rect(row.x, row.y + 3, row.width, 1), new Color(0.29f, 0.36f, 0.37f));
        }

        private void Concern(ref Card card, string id, string description)
        {
            Pair(ref card, id + "   " + description, scene.State.IsConcernResolved(id) ? "resolved" : "open");
        }

        private void Pair(ref Card card, string key, string value)
        {
            Rect row = card.Take(20);
            GUI.Label(row, key, small);
            GUI.Label(new Rect(row.xMax - 67, row.y, 67, row.height), value, body);
        }

        private void DrawStatusStrip(float width, float height, bool exploring)
        {
            string feedback = string.IsNullOrEmpty(scene.Feedback) ? scene.State.LastResult : scene.Feedback;
            string hint = scene.SetBackInteractionHint();
            if (hint == null && scene.State.Stage == ActionStage.Arrived && !scene.State.IsPaused)
                hint = scene.State.Active?.TargetId == SessionState.SetBackTarget ?
                    "At the maintenance door, face its control and press F to help Lin inspect the set." :
                    "Lin is at mark A.   [F] Start live tracking   [V] Debug-only mock E1";
            if (hint == null) hint = scene.World.Station
                ? "WASD  Move      E  Talk      F  Use maintenance station      Esc  Menu      F1  Monitor"
                : "WASD  Move      Mouse  Look      E  Talk to Lin      Esc  Menu      F1  Monitor";
            float feedbackHeight = string.IsNullOrEmpty(feedback) ? 0 :
                Mathf.Min(48, body.CalcHeight(new GUIContent(feedback), width - 48));
            float stripHeight = 12f + (exploring ? 20f : 0) + feedbackHeight;
            if (!exploring && feedbackHeight == 0) return;
            Rect strip = new Rect(12, height - stripHeight - 9, width - 24, stripHeight);
            Fill(strip, Surface);
            float y = strip.y + 6;
            if (exploring)
            {
                GUI.Label(new Rect(24, y, width - 48, 20), hint, small);
                y += 20;
            }
            if (feedbackHeight > 0) GUI.Label(new Rect(24, y, width - 48, feedbackHeight), feedback, body);
        }

        private void DrawWindow(float width, float height, bool sideBySide)
        {
            bool fault = scene.State.HasPause(PauseReason.Error);
            float cardWidth = Mathf.Min(370, width - 20);
            bool residenceIntro = scene.IntroOpen && scene.World.UsesSceneBindings;
            string introText = residenceIntro
                ? "Lin thinks this home is real. You're the actor playing his friend. Help him uncover the set and leave with you before 18:00."
                : scene.World.Station ? "At the maintenance door, let Lin see how the set was built. Walk behind it yourself to check the passage." :
                "Ask Lin to check the camera. After arrival, press F to run the live tracking experiment.";
            float introTextHeight = residenceIntro ? Mathf.Max(52, body.CalcHeight(new GUIContent(introText), cardWidth - 28)) : 52;
            float cardHeight = scene.State.TimedOut ? 150 : scene.IntroOpen ? 180 + introTextHeight :
                scene.MenuOpen ? 306 : fault ? 205 : scene.DialogueOpen ? 297 : 145;
            if (scene.State.CanRetryReturn && (scene.MenuOpen || fault)) cardHeight += 34;
            float x = sideBySide ? width - cardWidth - 12 : (width - cardWidth) / 2;
            float y = Mathf.Max(3, Mathf.Min(18, height - cardHeight - 3));
            var card = new Card(x, y, cardWidth, cardHeight, scene.State.TimedOut || fault ? Amber : Mint);
            // Include the clock and each independent owner even when only this card fits.
            GUI.Label(card.Take(19), scene.State.TimeHms + "   " + PauseOwners(), small);
            if (scene.State.TimedOut)
            {
                GUI.Label(card.Take(34), "Time expired", heading);
                GUI.Label(card.Take(40), "The director has returned. This run has ended.", body);
                if (Click(ref card, "Restart at 17:00   [R]")) scene.Restart();
            }
            else if (scene.IntroOpen)
            {
                GUI.Label(card.Take(36), "Before 18:00", heading);
                GUI.Label(card.Take(introTextHeight), introText, body);
                GUI.Label(card.Take(24), "WASD to move. Mouse to look.", small);
                GUI.Label(card.Take(24), scene.World.Station ? "E talk / resume.   3 camera.   6 maintenance door." :
                    residenceIntro ? "E talk / resume.   1 follow.   3 inspect camera." :
                    "E to talk, 3 to queue the check, E to resume.", small);
                GUI.Label(card.Take(24), scene.World.Station ? "F at the door control or passage sign to interact." :
                    "F runs live tracking A→B. V remains a debug-only mock completion.", small);
                if (Click(ref card, "Enter room   [Enter]")) scene.ToggleIntroOff();
            }
            else if (scene.MenuOpen)
            {
                GUI.Label(card.Take(32), "Menu", heading);
                if (Click(ref card, "Resume   [Esc]")) scene.ToggleMenu();
                if (scene.State.CanRetryReturn && Click(ref card, "Retry return to Lin's starting point")) scene.RetryReturn();
                if (Click(ref card, scene.TestPathBlocked ? "Test path: BLOCKED   /   Unblock" : "Test path: OPEN   /   Block"))
                    scene.ToggleTestPathBlocked();
                bool canComplete = scene.State.Stage == ActionStage.Arrived && scene.State.Active?.TargetId == SessionState.CameraTarget &&
                    !scene.DialogueOpen && !scene.ModelWait && !fault;
                bool enabledBefore = GUI.enabled;
                GUI.enabled = enabledBefore && canComplete;
                if (Click(ref card, "Resume + send mock E1 completion"))
                {
                    scene.ToggleMenu();
                    if (!scene.State.IsPaused) scene.TestCompleteCamera();
                }
                GUI.enabled = enabledBefore;
                if (Click(ref card, "Test concern: resolve C2")) scene.TestResolveC2();
                if (Click(ref card, "Test clock: jump near 18:00")) scene.TestNearDeadline();
                if (Click(ref card, "Restart at 17:00")) scene.Restart();
                GUI.Label(card.Take(28), "Other open windows keep simulation paused.", small);
            }
            else if (fault)
            {
                GUI.Label(card.Take(32), "Navigation stopped", heading);
                GUI.Label(card.Take(58), scene.State.LastResult, body);
                if (scene.TestPathBlocked && Click(ref card, "Unblock test path")) scene.ToggleTestPathBlocked();
                if (scene.State.CanRetryReturn && Click(ref card, "Retry return to Lin's starting point")) scene.RetryReturn();
                if (Click(ref card, "Acknowledge and return")) scene.RecoverNavigation();
            }
            else if (scene.DialogueOpen)
            {
                GUI.Label(card.Take(32), "Talk to Lin", heading);
                GUI.Label(card.Take(37), "Test input for C. Close dialogue to run the queued action.", body);
                bool enabledBefore = GUI.enabled;
                GUI.enabled = enabledBefore && !scene.ModelWait;
                Rect row = card.Take(34);
                if (Half(row, false, "1   Follow me")) scene.SubmitTestAction("follow");
                if (Half(row, true, "2   Stop here")) scene.SubmitTestAction("stop");
                if (scene.World.Station)
                {
                    row = card.Take(34);
                    if (Half(row, false, "3   Inspect camera")) scene.SubmitTestAction("inspect");
                    if (Half(row, true, "6   Inspect set")) scene.SubmitTestAction("inspect_set_back");
                }
                else if (Click(ref card, "3   Inspect camera_test")) scene.SubmitTestAction("inspect");
                row = card.Take(34);
                if (Half(row, false, "4   Withdraw")) scene.SubmitTestAction("withdraw");
                if (Half(row, true, "5   Restore talk")) scene.SubmitTestAction("restore");
                GUI.enabled = enabledBefore;
                if (Click(ref card, scene.ModelWait ? "End simulated model wait   [M]" : "Test model wait   [M]")) scene.ToggleModelWait();
                if (Click(ref card, "Close dialogue   [E]")) scene.ToggleDialogue();
                GUI.Label(card.Take(25), "Esc opens the menu over this dialogue.", small);
            }
            else if (scene.ModelWait)
            {
                GUI.Label(card.Take(30), "Simulated model wait", heading);
                GUI.Label(card.Take(40), "The clock and both characters are on hold.", body);
                if (Click(ref card, "Release wait   [M]")) scene.ToggleModelWait();
            }
        }

        private string PauseOwners()
        {
            if (scene.State.TimedOut) return "TIMEOUT";
            string owners = "";
            if (scene.IntroOpen) owners += "Intro  ";
            if (scene.DialogueOpen) owners += "Dialogue  ";
            if (scene.MenuOpen) owners += "Menu  ";
            if (scene.ModelWait) owners += "Model wait  ";
            if (scene.State.HasPause(PauseReason.Error)) owners += "Error";
            return owners;
        }

        private bool Click(ref Card card, string label)
        {
            Rect row = card.Take(34);
            row.height -= 3;
            return GUI.Button(row, label, button);
        }

        private bool Half(Rect row, bool right, string label)
        {
            float width = (row.width - 4) / 2;
            return GUI.Button(new Rect(row.x + (right ? width + 4 : 0), row.y, width, row.height - 3), label, button);
        }
    }
}
