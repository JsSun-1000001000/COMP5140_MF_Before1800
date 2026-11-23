using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Before1800.PartB
{
    public sealed partial class PlayerUIView
    {
        private static readonly Color Cream = new Color32(255, 247, 231, 255);
        private static readonly Color Soft = new Color32(224, 206, 189, 255);
        private static readonly Color Apricot = new Color32(239, 187, 140, 255);
        private static readonly Color Ink = new Color32(54, 39, 36, 255);
        private static readonly Color Paper = new Color32(244, 232, 209, 255);
        private static readonly Color Plate = new Color32(44, 35, 34, 246);
        private static readonly Color DarkButton = new Color32(73, 55, 51, 255);
        private static readonly Color BrownRule = new Color32(136, 109, 89, 220);

        private sealed class Control
        {
            internal RectTransform Node;
            internal Button Button;
            internal Text Label;
        }

        private RectTransform clockRail, keyBadge, journalPlate, modalPlate, introPlate;
        private RectTransform introBand, talkBand, bookBand, modalBand;
        private Text journalPlace, bookTitle, modalStamp;
        private RectTransform journalDivider;
        private RectTransform speakerDash;

        private void BuildCanvas()
        {
            if (!EventSystem.current)
            {
                var events = new GameObject("PartB UI Events", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            canvasRect = Node(transform, "PlayerUI");
            var canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 20;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = .6f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();

            top = FillNode(canvasRect, "Clock and notebook");
            clockPlate = Panel(top, "Time", Plate);
            clockRail = Panel(clockPlate, "Clock rule", Apricot);
            clockText = Label(clockPlate, "ClockText", "", 26, Cream);
            clockCaption = Label(clockPlate, "ClockCaption", "", 15, Soft);
            journal = MakeButton(top, "JournalButton", "手记   J", () => flow.ToggleJournal());
            explore = FillNode(canvasRect, "Exploration");
            crosshair = Panel(explore, "Aim", Cream);
            var edge = crosshair.gameObject.AddComponent<Shadow>();
            edge.effectColor = new Color(0.15f, .11f, .09f, .8f);
            edge.effectDistance = new Vector2(1,-1);
            hintPlate = Panel(explore, "WorldPrompt", Plate);
            keyBadge = Panel(hintPlate, "Use key", Apricot);
            Fill(Label(keyBadge, "Key", "E", 17, Ink, TextAnchor.MiddleCenter).rectTransform);
            hint = Label(hintPlate, "Action", "", 19, Cream);
            noticePlate = Panel(explore, "SceneFeedback", Plate);
            notice = Label(noticePlate, "Feedback", "", 17, Soft);

            intro = FillNode(canvasRect, "Opening");
            Shade(intro, "Curtain", new Color32(30, 23, 25, 70), new Color32(30, 23, 25, 7), false);
            introPlate = Panel(intro, "Opening card", Plate);
            introBand = Panel(introPlate, "Opening accent", Apricot);
            introPlace = Label(introPlate, "Location", "", 16, Apricot);
            introTitle = Label(introPlate, "Title", "18 点之前", 52, Cream);
            introSynopsis = Label(introPlate, "Story", "林以为这里是自己的家。\n你知道这间屋子是片场，也曾帮剧组隐瞒真相。", 19, Cream);
            introGoal = Label(introPlate, "Objective", "", 16, Soft);
            start = MakeButton(introPlate, "StartExploring", "开始探索   Enter", () => flow.Begin(), true);
            introControls = Label(introPlate, "Controls", "WASD 移动   鼠标环顾", 15, Soft, TextAnchor.MiddleRight);

            talk = FillNode(canvasRect, "Conversation");
            talkBand = Panel(talk, "Dialogue matte", Plate);
            talkRule = Panel(talkBand, "Dialogue hairline", BrownRule);
            speaker = Label(talkBand, "Speaker", "", 18, Apricot);
            speakerDash = Panel(talkBand, "Speaker dash", Apricot);
            readEarlier = MakeButton(talkBand, "PreviousLines", "上一段", () => ScrollLine(1));
            readLater = MakeButton(talkBand, "MoreLines", "下一段", () => ScrollLine(-1));
            readHint = Label(talkBand, "ReadingHint", "滚轮阅读", 15, Soft, TextAnchor.MiddleRight);
            var viewport = Panel(talkBand, "SpokenTextArea", Color.clear);
            viewport.GetComponent<Image>().raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            lineScroll = viewport.gameObject.AddComponent<ScrollRect>();
            lineScroll.horizontal = false;
            lineScroll.movementType = ScrollRect.MovementType.Clamped;
            lineScroll.scrollSensitivity = 34f;
            lineScroll.viewport = viewport;
            line = Label(viewport, "SpokenLine", "", 26, Cream, TextAnchor.UpperLeft);
            lineScroll.content = line.rectTransform;
            talkNotice = Label(talkBand, "DialogueFeedback", "", 15, Soft);
            for (int i = 0; i < talkChoices.Length; i++)
            {
                int index = i;
                talkChoices[i] = MakeButton(talkBand, "DialogueChoice" + (i+1), "", () => Choose(index));
            }
            input = MakeInput(talkBand);
            send = MakeButton(talkBand, "SendDialogue", "发送", SendDraft);
            exitTalk = MakeButton(talkBand, "LeaveDialogue", "结束交谈   Esc", () => flow.Back());

            book = FillNode(canvasRect, "Notebook");
            Shade(book, "Notebook backdrop", new Color32(27, 19, 20, 94), new Color32(27, 19, 20, 94), false);
            journalPlate = Panel(book, "Notebook paper", Paper);
            bookBand = Panel(journalPlate, "Notebook binding", new Color32(155, 104, 79, 255));
            journalPlace = Label(journalPlate, "NotesLocation", "", 15, new Color32(104, 79, 64, 255));
            bookTitle = Label(journalPlate, "Notebook title", "现场手记", 34, Ink);
            journalDivider = Panel(journalPlate, "Notebook rule", new Color32(168, 142, 115, 255));
            for (int i = 0; i < sectionBody.Length; i++)
            {
                sectionTitles[i] = Label(journalPlate, "Heading"+i, "", 16, new Color32(126, 74, 49, 255));
                sectionBody[i] = Label(journalPlate, "Note"+i, "", 19, Ink, TextAnchor.UpperLeft);
                sectionRules[i] = Panel(journalPlate, "Section line"+i, new Color32(202, 180, 153, 255));
            }
            closeBook = MakeButton(journalPlate, "CloseJournal", "收起手记   Esc", () => flow.Back());

            modal = FillNode(canvasRect, "Menu and interruptions");
            Shade(modal, "Pause backdrop", new Color32(31, 23, 24, 135), new Color32(31, 23, 24, 95), false);
            modalPlate = Panel(modal, "Menu card", Plate);
            modalBand = Panel(modalPlate, "Page marker", Apricot);
            modalStamp = Label(modalPlate, "ModalTime", "", 18, Apricot);
            modalTitle = Label(modalPlate, "ModalTitle", "", 36, Cream);
            modalBody = Label(modalPlate, "ModalDescription", "", 19, Soft, TextAnchor.UpperLeft);
            for (int i = 0; i < modalChoices.Length; i++)
            {
                int index = i;
                modalChoices[i] = MakeButton(modalPlate, "MenuChoice"+(i+1), "", () => Choose(index), i==0);
            }
        }

        private InputField MakeInput(RectTransform parent)
        {
            var box = Panel(parent, "DialogueInput", new Color32(37, 29, 29, 255));
            var field = box.gameObject.AddComponent<InputField>();
            var image = box.GetComponent<Image>();
            image.raycastTarget = true;
            field.targetGraphic = image;
            var fieldOutline = box.gameObject.AddComponent<Outline>();
            fieldOutline.effectDistance = new Vector2(1, -1);
            fieldOutline.effectColor = new Color32(133, 107, 91, 255);
            var typed = Label(box, "Draft", "", 18, Cream);
            Fill(typed.rectTransform, 12, 2);
            placeholder = Label(box, "Prompt", "", 17, new Color32(193,172,157,255));
            Fill(placeholder.rectTransform, 12, 2);
            field.textComponent = typed;
            field.placeholder = placeholder;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;
            field.characterLimit = 500;
            field.customCaretColor = true;
            field.caretColor = Apricot;
            field.selectionColor = new Color32(191, 139, 103, 140);
            field.onValueChanged.AddListener(value => flow.Draft = value);
            field.onSubmit.AddListener(_ => SendDraft());
            return field;
        }

        private static void Fill(RectTransform r, float x = 0, float y = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(x, y);
            r.offsetMax = new Vector2(-x, -y);
        }

        private static RectTransform Node(Transform parent, string name)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            return r;
        }

        private static RectTransform FillNode(Transform parent, string name)
        {
            var r = Node(parent, name);
            Fill(r);
            return r;
        }

        private static RectTransform Panel(Transform parent, string name, Color color)
        {
            var r = Node(parent, name);
            var image = r.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return r;
        }

        private static void Shade(RectTransform parent, string name, Color left, Color right, bool vertical)
        {
            var rect = FillNode(parent, name);
            var shade = rect.gameObject.AddComponent<PlayerUIViewGradient>();
            shade.Set(left, right, vertical);
        }

        private Text Label(Transform parent, string name, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = Node(parent, name).gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            // Source Han Sans has a larger metric box than its visible glyphs.
            // Keep short headings and button captions visible at all UI scales.
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.lineSpacing = 1.08f;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        private Control MakeButton(RectTransform parent, string name, string value, Action act, bool primary=false)
        {
            var r = Panel(parent, name, Color.white);
            var paint = r.GetComponent<Image>();
            paint.raycastTarget = true;
            var button = r.gameObject.AddComponent<Button>();
            button.targetGraphic = paint;
            var colors = button.colors;
            colors.normalColor = primary ? Apricot : DarkButton;
            colors.highlightedColor = primary ? new Color32(255,216,174,255) : new Color32(110,81,68,255);
            colors.selectedColor = primary ? new Color32(252,210,169,255) : new Color32(104,74,64,255);
            colors.pressedColor = primary ? new Color32(224,164,111,255) : new Color32(129,92,73,255);
            colors.disabledColor = new Color32(55,45,43,255);
            colors.fadeDuration = .08f;
            button.colors = colors;
            var stroke = r.gameObject.AddComponent<Outline>();
            stroke.effectColor = primary ? new Color32(255,218,172,210) : new Color32(145,113,90,200);
            stroke.effectDistance = new Vector2(1f,-1f);
            var text = Label(r, "Label", value, 17, primary ? Ink : Cream, TextAnchor.MiddleCenter);
            Fill(text.rectTransform, 7f, 1f);
            button.onClick.AddListener(() => { act(); Refresh(); });
            return new Control { Node = r, Button = button, Label = text };
        }

        private static void At(RectTransform rect, float left, float topOffset, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(left,-topOffset);
            rect.sizeDelta = new Vector2(Mathf.Max(width,1), Mathf.Max(height,1));
        }

        private void Layout(float width, float height)
        {
            if (width < 1 || height < 1) return;
            float margin = width < 760 ? 17 : 32;
            At(clockPlate,margin,20,Mathf.Min(width*.48f,231),44);
            At(clockRail,0,0,3,44);
            At(clockText.rectTransform,14,0,88,44);
            At(clockCaption.rectTransform,98,0,127,44);
            At(journal.Node,width-margin-100,20,100,43);
            At(crosshair,width*.5f-2,height*.5f-2,4,4);
            float hintWidth = Mathf.Min(width-margin*2,Mathf.Max(190,Mathf.Min(440, hint.preferredWidth+75)));
            At(hintPlate,(width-hintWidth)/2,height*.67f,hintWidth,44);
            At(keyBadge,9,7,30,30);
            At(hint.rectTransform,51,0,hintWidth-59,44);
            float noticeWidth=Mathf.Min(width-margin*2,Mathf.Max(220,notice.preferredWidth+40));
            At(noticePlate,margin,height-80,noticeWidth,53);
            At(notice.rectTransform,16,0,noticeWidth-32,53);

            float cardWidth = Mathf.Min(467,width-margin*2);
            float storyHeight=Mathf.Max(64,FittedHeight(introSynopsis,cardWidth-58));
            float goalY=125+storyHeight+4;
            float goalHeight=Mathf.Max(40,FittedHeight(introGoal,cardWidth-58));
            float beginY=goalY+goalHeight+14;
            float introHeight=beginY+67;
            At(introPlate,Mathf.Min(margin*1.5f,width-cardWidth-margin),Mathf.Max(12,height-introHeight-42),cardWidth,introHeight);
            At(introBand,0,0,4,introHeight);
            At(introPlace.rectTransform,28,18,cardWidth-56,28);
            At(introTitle.rectTransform,26,44,cardWidth-52,72);
            At(introSynopsis.rectTransform,29,125,cardWidth-58,storyHeight);
            At(introGoal.rectTransform,29,goalY,cardWidth-58,goalHeight);
            At(start.Node,29,beginY,Mathf.Min(208,cardWidth-58),44);
            Active(introControls.rectTransform,cardWidth>=405);
            At(introControls.rectTransform,245,beginY,cardWidth-273,44);

            LayoutDialogue(width,height);
            float bookWidth=Mathf.Min(490,width-margin*2);
            float bookHeight=Mathf.Min(550,height-92);
            At(journalPlate,width-margin-bookWidth,(height-bookHeight)*.56f,bookWidth,bookHeight);
            At(bookBand,0,0,7,bookHeight);
            float x=32, inside=bookWidth-64;
            Set(journalPlace,openingPlace);
            At(journalPlace.rectTransform,x,18,inside,28);
            At(bookTitle.rectTransform,x,46,inside,49);
            At(journalDivider,x,102,inside,1);
            float y=116;
            for (int i = 0; i < sectionBody.Length; i++)
            {
                At(sectionTitles[i].rectTransform,x,y,inside,26);
                float bodyHeight = Mathf.Max(31, FittedHeight(sectionBody[i], inside));
                At(sectionBody[i].rectTransform,x,y+31,inside,bodyHeight);
                y+=bodyHeight+43;
                At(sectionRules[i],x,y,inside,1);
                y+=11;
            }
            At(closeBook.Node,x,bookHeight-64,158,42);

            float modalWidth=Mathf.Min(450,width-margin*2);
            int modalCount = modal.gameObject.activeSelf ? choiceCount : 1;
            float bodyHeightForModal=string.IsNullOrEmpty(modalBody.text) ? 0 : FittedHeight(modalBody,modalWidth-52)+14;
            float actionTop=114+bodyHeightForModal;
            float modalHeight=actionTop+modalCount*53+15;
            At(modalPlate,(width-modalWidth)*.5f,(height-modalHeight)*.5f,modalWidth,modalHeight);
            At(modalBand,0,0,modalWidth,3);
            At(modalStamp.rectTransform,26,17,modalWidth-52,27);
            Set(modalStamp,flow.ClockText);
            At(modalTitle.rectTransform,26,51,modalWidth-52,57);
            At(modalBody.rectTransform,26,111,modalWidth-52,bodyHeightForModal);
            for(int i=0;i<modalChoices.Length;i++) At(modalChoices[i].Node,26,actionTop+i*53,modalWidth-52,43);
        }

        private void LayoutDialogue(float width,float height)
        {
            float inside=Mathf.Min(width-44,1060);
            float left=(width-inside)*.5f;
            float y=16;
            At(speakerDash,left,y+15,24,2);
            At(speaker.rectTransform,left+35,y,inside-35,32);
            y+=36;
            float lineHeight=Mathf.Max(41, FittedHeight(line,inside));
            float visibleHeight=Mathf.Min(lineHeight,Mathf.Clamp(height*.13f,72,108));
            bool overflow=lineHeight>visibleHeight+1;
            Active(readEarlier.Node,overflow);
            Active(readLater.Node,overflow);
            Active(readHint.rectTransform,overflow);
            At(readEarlier.Node,left+inside-170,12,78,31);
            At(readLater.Node,left+inside-82,12,82,31);
            At(readHint.rectTransform,left+inside-262,12,80,31);
            At(lineScroll.viewport,left,y,inside,visibleHeight);
            At(line.rectTransform,0,0,inside,lineHeight);
            lineScroll.vertical=overflow;
            if(resetReadingPosition || !overflow)
            {
                lineScroll.StopMovement();
                lineScroll.verticalNormalizedPosition=1f;
                resetReadingPosition=false;
            }
            y+=visibleHeight+9;
            bool hasNotice=!string.IsNullOrWhiteSpace(talkNotice.text) && talkNotice.text!=line.text;
            Active(talkNotice.rectTransform,hasNotice);
            if(hasNotice)
            {
                float feedbackHeight=Mathf.Max(24,FittedHeight(talkNotice,inside));
                At(talkNotice.rectTransform,left,y,inside,feedbackHeight);
                y+=feedbackHeight+4;
            }
            float offset=0;
            if(talk.gameObject.activeSelf)
            {
                for(int i=0;i<choiceCount;i++)
                {
                    var choice=talkChoices[i];
                    float scale = canvasRect.GetComponent<Canvas>().scaleFactor;
                    choice.Label.fontSize = Mathf.Clamp(Mathf.CeilToInt(15f / Mathf.Max(scale,.1f)), 18, 23);
                    float w=Mathf.Min(inside,Mathf.Max(110,choice.Label.preferredWidth+30));
                    if(offset>0 && offset+w>inside){y+=47;offset=0;}
                    At(choice.Node,left+offset,y,w,39);
                    offset+=w+10;
                }
                if(choiceCount>0)y+=49;
            }
            float leaveWidth=153,sendWidth=68;
            float inputWidth=inside-leaveWidth-sendWidth-20;
            At(input.GetComponent<RectTransform>(),left,y,inputWidth,39);
            At(send.Node,left+inputWidth+9,y,sendWidth,39);
            At(exitTalk.Node,left+inside-leaveWidth,y,leaveWidth,39);
            float matteHeight=y+59;
            At(talkBand,0,height-matteHeight,width,matteHeight);
            At(talkRule,0,0,width,1);
        }

        private void ScrollLine(int direction)
        {
            float viewportHeight = lineScroll.viewport.rect.height;
            float overflow = line.rectTransform.rect.height - viewportHeight;
            if (overflow <= 0) return;
            lineScroll.StopMovement();
            lineScroll.verticalNormalizedPosition = Mathf.Clamp01(lineScroll.verticalNormalizedPosition +
                direction * (viewportHeight - line.fontSize) / overflow);
        }

        private static float FittedHeight(Text label,float width)
        {
            if(string.IsNullOrEmpty(label.text)) return 0;
            var settings=label.GetGenerationSettings(new Vector2(width,0));
            return Mathf.Ceil(label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text,settings)/label.pixelsPerUnit);
        }
    }
}
