using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Before1800
{
    public sealed class PrototypeView : MonoBehaviour
    {
        public Action OnBegin, OnTalk, OnSend, OnContinue, OnCancel, OnRetry, OnResume, OnRestart, OnQuit, OnCancelInspection;
        public Action<string> OnQuickInput;
        public InputField Input { get; private set; }
        public bool DialogueVisible => dialoguePanel != null && dialoguePanel.activeSelf;
        public bool MenuVisible => menuPanel != null && menuPanel.activeSelf;
        public bool IntroVisible => introPanel != null && introPanel.activeSelf;
        public bool DebugVisible => debugPanel != null && debugPanel.activeSelf;
        private Canvas canvas;
        private Font font;
        private GameObject dialoguePanel, menuPanel, introPanel, debugPanel;
        private Text clock, pauseLabel, objective, facts, hint, transcript, dialogueStatus, serviceLabel, debugText, toast;
        private ScrollRect transcriptScroll;
        private Button send, next, cancel, retry;
        private Color panel = new Color(.055f, .076f, .09f, .97f);
        private Color muted = new Color(.64f, .73f, .75f);
        private Color accent = new Color(.45f, .89f, .77f);
        private float toastUntil;

        public void Build()
        {
            font = Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei", "Segoe UI", "Arial"},18);
            if(font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Interface", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var top = Box("Header", root.transform, new Vector2(.5f,1), new Vector2(.5f,1), new Vector2(1600,105), Vector2.zero, new Color(.025f,.04f,.05f,.9f));
            Label(top.transform,"BEFORE 18:00",32,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(700,45),new Vector2(34,-18));
            Label(top.transform,"E1 PROTOTYPE  /  A CAMERA THAT FOLLOWS",14,accent,new Vector2(0,1),new Vector2(0,1),new Vector2(800,25),new Vector2(36,-64));
            clock = Label(top.transform,"17:00",38,Color.white,new Vector2(1,1),new Vector2(1,1),new Vector2(170,48),new Vector2(-32,-14),TextAnchor.MiddleRight);
            pauseLabel = Label(top.transform,"EXPLORING",13,accent,new Vector2(1,1),new Vector2(1,1),new Vector2(400,24),new Vector2(-36,-65),TextAnchor.MiddleRight);
            Label(root.transform,"+",25,new Color(1,1,1,.7f),new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(35,35),Vector2.zero,TextAnchor.MiddleCenter);

            var notes = Box("Notes",root.transform,new Vector2(0,0),Vector2.zero,new Vector2(390,173),new Vector2(26,26),panel);
            Label(notes.transform,"SHARED EVIDENCE",14,accent,new Vector2(0,1),new Vector2(0,1),new Vector2(350,24),new Vector2(20,-15));
            facts = Label(notes.transform,"E1 / Not discovered",20,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(350,56),new Vector2(20,-46));
            objective = Label(notes.transform,"Find the camera. Let Lin see it for himself.",16,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(350,58),new Vector2(20,-103));
            hint = Label(root.transform,"WASD  Move     Mouse  Look     E  Interact",17,Color.white,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(760,45),new Vector2(100,28),TextAnchor.MiddleCenter);
            serviceLabel = Label(root.transform,"API: checking local service",14,muted,new Vector2(1,0),new Vector2(1,0),new Vector2(480,27),new Vector2(-28,4),TextAnchor.MiddleRight);
            toast = Label(root.transform,"",21,accent,new Vector2(.5f,.77f),new Vector2(.5f,.5f),new Vector2(940,55),Vector2.zero,TextAnchor.MiddleCenter);
            Label(root.transform,"T  Talk to Lin    F3  Trace    Esc  Menu",14,muted,new Vector2(1,0),new Vector2(1,0),new Vector2(440,30),new Vector2(-28,45),TextAnchor.MiddleRight);

            BuildDialogue(root.transform);
            BuildIntro(root.transform);
            BuildMenu(root.transform);
            debugPanel = Box("Trace",root.transform,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(690,655),new Vector2(24,20),new Color(.02f,.035f,.045f,.97f));
            Label(debugPanel.transform,"INPUT / DECISION / WORLD FEEDBACK",17,accent,new Vector2(0,1),new Vector2(0,1),new Vector2(640,40),new Vector2(22,-14));
            debugText = Label(debugPanel.transform,"",14,new Color(.83f,.9f,.9f),new Vector2(0,1),new Vector2(0,1),new Vector2(646,570),new Vector2(22,-64));
            debugPanel.SetActive(false);
        }

        private void BuildDialogue(Transform parent)
        {
            dialoguePanel = Box("Dialogue",parent,new Vector2(1,.5f),new Vector2(1,.5f),new Vector2(720,710),new Vector2(-26,-15),panel);
            Label(dialoguePanel.transform,"LIN",31,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(630,45),new Vector2(28,-20));
            dialogueStatus = Label(dialoguePanel.transform,"The world is paused while you talk.",15,accent,new Vector2(0,1),new Vector2(0,1),new Vector2(664,30),new Vector2(28,-68));
            BuildTranscript(dialoguePanel.transform);
            Label(dialoguePanel.transform,"YOUR WORDS",13,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(660,25),new Vector2(28,-388));
            var inputObject = Box("PlayerInput",dialoguePanel.transform,new Vector2(0,1),new Vector2(0,1),new Vector2(664,105),new Vector2(28,-420),new Color(.1f,.14f,.16f));
            Input = inputObject.AddComponent<InputField>(); Input.lineType = InputField.LineType.MultiLineNewline; Input.characterLimit = 1600;
            var inputText = Label(inputObject.transform,"",19,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(632,83),new Vector2(16,-10));
            Input.textComponent = inputText;
            var placeholder = Label(inputObject.transform,"Ask Lin to inspect the camera, or discuss what he has seen…",18,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(632,83),new Vector2(16,-10));
            placeholder.fontStyle = FontStyle.Italic; Input.placeholder = placeholder;
            Input.targetGraphic = inputObject.GetComponent<Image>();
            ButtonAt(dialoguePanel.transform,"Invite to inspect",new Vector2(28,-546),new Vector2(205,36),()=>OnQuickInput?.Invoke("Please inspect the camera. Move between the two marked points and watch whether its lens follows you."),false);
            ButtonAt(dialoguePanel.transform,"Discuss what happened",new Vector2(245,-546),new Vector2(245,36),()=>OnQuickInput?.Invoke("What did you actually see just now? What does it prove, and what are you still unsure about?"),false);
            send = ButtonAt(dialoguePanel.transform,"Send",new Vector2(28,-602),new Vector2(145,49),()=>OnSend?.Invoke(),true);
            next = ButtonAt(dialoguePanel.transform,"Continue / act",new Vector2(187,-602),new Vector2(205,49),()=>OnContinue?.Invoke(),true);
            retry = ButtonAt(dialoguePanel.transform,"Retry",new Vector2(406,-602),new Vector2(120,49),()=>OnRetry?.Invoke(),false);
            cancel = ButtonAt(dialoguePanel.transform,"Close",new Vector2(540,-602),new Vector2(152,49),()=>OnCancel?.Invoke(),false);
            Label(dialoguePanel.transform,"Closing a pending reply cancels its proposed action.",13,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(664,25),new Vector2(28,-667));
            dialoguePanel.SetActive(false);
        }

        private void BuildTranscript(Transform parent)
        {
            var scrollObject = Box("ConversationScroll",parent,new Vector2(0,1),new Vector2(0,1),new Vector2(664,260),new Vector2(28,-116),new Color(0,0,0,0));
            transcriptScroll = scrollObject.AddComponent<ScrollRect>();
            transcriptScroll.horizontal = false;
            transcriptScroll.vertical = true;
            transcriptScroll.movementType = ScrollRect.MovementType.Clamped;
            transcriptScroll.scrollSensitivity = 28;

            var viewport = new GameObject("Viewport",typeof(RectTransform),typeof(RectMask2D));
            viewport.transform.SetParent(scrollObject.transform,false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero; viewportRect.offsetMax = new Vector2(-20,0);

            transcript = Label(viewport.transform,"",21,Color.white,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,Vector2.zero);
            var content = transcript.rectTransform;
            content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f,1); content.sizeDelta = new Vector2(-8,0);
            var fitter = transcript.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            transcriptScroll.viewport = viewportRect;
            transcriptScroll.content = content;

            var track = Box("ConversationScrollbar",scrollObject.transform,new Vector2(1,1),new Vector2(1,1),new Vector2(12,260),Vector2.zero,new Color(.15f,.21f,.23f));
            var sliding = new GameObject("SlidingArea",typeof(RectTransform));
            sliding.transform.SetParent(track.transform,false);
            var slidingRect = sliding.GetComponent<RectTransform>();
            slidingRect.anchorMin = Vector2.zero; slidingRect.anchorMax = Vector2.one;
            slidingRect.offsetMin = new Vector2(2,2); slidingRect.offsetMax = new Vector2(-2,-2);
            var handle = Box("Handle",sliding.transform,Vector2.zero,new Vector2(.5f,.5f),Vector2.zero,Vector2.zero,muted);
            var scrollbar = track.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            transcriptScroll.verticalScrollbar = scrollbar;
            transcriptScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private void BuildIntro(Transform parent)
        {
            introPanel = Box("Introduction",parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(760,520),Vector2.zero,panel);
            Label(introPanel.transform,"A FRIEND. A CAMERA. A QUESTION.",24,accent,new Vector2(0,1),new Vector2(0,1),new Vector2(690,55),new Vector2(34,-28));
            Label(introPanel.transform,"Before 18:00",47,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(690,70),new Vector2(34,-93));
            Label(introPanel.transform,"You know this home is a set. Lin does not.\n\nFind the camera, invite him to inspect it, and let him observe it from two positions. Then ask what he has learned.\n\nTalking pauses the world. Only a completed observation counts as evidence.",21,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(690,230),new Vector2(34,-180));
            ButtonAt(introPanel.transform,"Enter the room",new Vector2(34,-438),new Vector2(260,54),()=>OnBegin?.Invoke(),true);
            Label(introPanel.transform,"WASD / Mouse / E / T",16,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(380,45),new Vector2(330,-444),TextAnchor.MiddleRight);
        }

        private void BuildMenu(Transform parent)
        {
            menuPanel = Box("PauseMenu",parent,new Vector2(.5f,.5f),new Vector2(.5f,.5f),new Vector2(460,400),Vector2.zero,panel);
            Label(menuPanel.transform,"PAUSED",30,Color.white,new Vector2(0,1),new Vector2(0,1),new Vector2(400,55),new Vector2(30,-25));
            ButtonAt(menuPanel.transform,"Resume",new Vector2(30,-100),new Vector2(400,48),()=>OnResume?.Invoke(),true);
            ButtonAt(menuPanel.transform,"Cancel inspection",new Vector2(30,-160),new Vector2(400,48),()=>OnCancelInspection?.Invoke(),false);
            ButtonAt(menuPanel.transform,"Restart this session",new Vector2(30,-220),new Vector2(400,48),()=>OnRestart?.Invoke(),false);
            ButtonAt(menuPanel.transform,"Quit",new Vector2(30,-280),new Vector2(400,48),()=>OnQuit?.Invoke(),false);
            Label(menuPanel.transform,"Restarting clears this run and rejects late API replies.",14,muted,new Vector2(0,1),new Vector2(0,1),new Vector2(400,40),new Vector2(30,-346));
            menuPanel.SetActive(false);
        }

        public void ShowIntro(bool show) { introPanel.SetActive(show); }
        public void ShowMenu(bool show) { menuPanel.SetActive(show); }
        public void ShowDialogue(bool show)
        {
            dialoguePanel.SetActive(show);
            if(show) { hint.text=""; toast.text=""; toastUntil=0; }
        }
        public void ToggleDebug() { debugPanel.SetActive(!debugPanel.activeSelf); }
        public void SetConversation(string text, string status, bool waiting, bool hasDecision, bool error)
        {
            transcript.text = text; dialogueStatus.text = status;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(transcript.rectTransform);
            transcriptScroll.StopMovement();
            transcriptScroll.verticalNormalizedPosition = 1;
            Input.interactable = !waiting && !hasDecision;
            send.interactable = !waiting && !hasDecision;
            next.interactable = hasDecision;
            retry.interactable = error && !waiting;
            cancel.GetComponentInChildren<Text>().text = waiting ? "Cancel" : "Close";
        }
        public void SetService(string text, bool ready) { serviceLabel.text = text; serviceLabel.color = ready ? accent : new Color(1,.71f,.4f); }
        public void SetHint(string text) { hint.text = text; }
        public void ShowToast(string text) { toast.text = text; toastUntil = Time.unscaledTime + 5; }
        private void Update() { if (toast != null && Time.unscaledTime > toastUntil) toast.text = ""; }

        public void Refresh(GameSnapshot snapshot, bool paused, string phase, string trace)
        {
            int seconds = Mathf.FloorToInt(snapshot.elapsed_game_seconds);
            clock.text = string.Format("{0:00}:{1:00}", 17 + seconds / 3600, (seconds % 3600) / 60);
            pauseLabel.text = paused ? "WORLD PAUSED" : "EXPLORING";
            bool found = Array.IndexOf(snapshot.player_discovered,"E1") >= 0;
            bool verified = Array.IndexOf(snapshot.verified_evidence,"E1") >= 0;
            facts.text = verified ? "E1 / VERIFIED TOGETHER" : found ? "E1 / Found, not verified" : "E1 / Not discovered";
            facts.color = verified ? accent : Color.white;
            objective.text = verified ? "Talk to Lin again. His next response receives the observation result." : found ? "Invite Lin to inspect the lens from both marked positions." : "Find the camera mounted on the far wall.";
            debugText.text = "SESSION  " + snapshot.session_id.Substring(0,Math.Min(8,snapshot.session_id.Length)) + "   VERSION " + snapshot.state_version + "\n" +
                "FACTS  [" + string.Join(", ",snapshot.verified_evidence) + "]\n" +
                "C1 " + snapshot.concerns.C1 + " / C2 " + snapshot.concerns.C2 + " / C3 " + snapshot.concerns.C3 + "\n" +
                "MODE " + snapshot.cooperation_mode + " / ROUTE " + snapshot.route_confirmed + " / CONSENT " + snapshot.escape_agreed + "\n" +
                "CAMERA  " + phase + "\n\n" + trace;
        }

        private GameObject Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position, Color color)
        {
            var obj = new GameObject(name,typeof(RectTransform),typeof(Image)); obj.transform.SetParent(parent,false);
            Place(obj.GetComponent<RectTransform>(),anchor,pivot,size,position); obj.GetComponent<Image>().color = color; return obj;
        }
        private Text Label(Transform parent,string text,int size,Color color,Vector2 anchor,Vector2 pivot,Vector2 dimensions,Vector2 position,TextAnchor alignment=TextAnchor.UpperLeft)
        {
            var obj = new GameObject("Text",typeof(RectTransform),typeof(Text)); obj.transform.SetParent(parent,false);
            Place(obj.GetComponent<RectTransform>(),anchor,pivot,dimensions,position);
            var label = obj.GetComponent<Text>(); label.font = font; label.text = text; label.fontSize = size; label.color = color;
            label.alignment = alignment; label.raycastTarget = false; label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        private Button ButtonAt(Transform parent,string label,Vector2 pos,Vector2 size,Action action,bool primary)
        {
            var obj = Box(label,parent,new Vector2(0,1),new Vector2(0,1),size,pos,primary ? accent : new Color(.15f,.21f,.23f));
            var button = obj.AddComponent<Button>(); button.targetGraphic = obj.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(.9f,1,1); colors.disabledColor = new Color(.45f,.48f,.49f,.5f); button.colors = colors;
            button.onClick.AddListener(()=>action());
            Label(obj.transform,label,17,primary ? new Color(.03f,.07f,.07f) : Color.white,new Vector2(.5f,.5f),new Vector2(.5f,.5f),size,Vector2.zero,TextAnchor.MiddleCenter);
            return button;
        }
        private static void Place(RectTransform rect,Vector2 anchor,Vector2 pivot,Vector2 size,Vector2 position)
        { rect.anchorMin=anchor; rect.anchorMax=anchor; rect.pivot=pivot; rect.sizeDelta=size; rect.anchoredPosition=position; }
    }
}
