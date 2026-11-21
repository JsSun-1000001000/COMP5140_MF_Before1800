using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Before1800.PartB
{
    // The view projects scene state. Buttons never write evidence or advance the clock.
    public sealed partial class PlayerUIView : MonoBehaviour
    {
        private PartBScene scene;
        private PlayerInteraction flow;
        private RectTransform canvasRect;
        private RectTransform top, clockPlate, explore, talk, book, modal, intro, noticePlate, hintPlate;
        private RectTransform crosshair, talkRule;
        private Text clockText, clockCaption, speaker, line, notice, hint, talkNotice;
        private Text introPlace, introTitle, introSynopsis, introGoal, introControls, modalTitle, modalBody, placeholder;
        private readonly Text[] sectionTitles = new Text[3], sectionBody = new Text[3];
        private readonly RectTransform[] sectionRules = new RectTransform[3];
        private readonly Control[] talkChoices = new Control[3], modalChoices = new Control[3];
        private Control journal, start, exitTalk, send, closeBook;
        private InputField input;
        private ScrollRect lineScroll;
        private Text readHint;
        private Control readEarlier, readLater;
        private Font font;
        private PlayerScreen? shown;
        private string previousJournal, previousSpeakerLine, previousTalkNotice;
        private Vector2 lastSize;
        private int choiceCount, lastCompositionFrame = -10, submitFrame = -10;
        private bool changedLayout, resetReadingPosition;
        private bool visible = true;
        private string openingPlace;

        public bool Editing => visible && input && input.isFocused;

        public void Bind(PartBScene owner, PlayerInteraction interaction)
        {
            scene = owner;
            flow = interaction;
            if (!canvasRect)
            {
                font = PlayerUIFont.Get();
                BuildCanvas();
            }
            openingPlace = scene.World.UsesSceneBindings ? "住宅  /  客厅" : "片场  /  调查室";
            introPlace.text = openingPlace;
            previousJournal = null;
            shown = null;
            Refresh();
        }

        public void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            if (!canvasRect) return;
            if (!value) BlurInput();
            canvasRect.gameObject.SetActive(value);
            if (value)
            {
                shown = null;
                Refresh();
            }
        }

        public void BlurInput()
        {
            if (input && input.isFocused)
            {
                string text = input.text;
                input.DeactivateInputField();
                if (flow != null) flow.Draft = text;
            }
            if (EventSystem.current && input && EventSystem.current.currentSelectedGameObject == input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        private void LateUpdate()
        {
            if (flow == null || !visible) return;
            if (Editing && !string.IsNullOrEmpty(Input.compositionString)) lastCompositionFrame = Time.frameCount;
            Refresh();
            if (Input.GetKeyDown(KeyCode.Tab) && string.IsNullOrEmpty(Input.compositionString))
                AdvanceTab(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        }

        private void Refresh()
        {
            if (!canvasRect || flow == null || !visible) return;
            var screen = flow.Screen;
            bool transition = shown != screen;
            if (transition)
            {
                if (shown == PlayerScreen.Talk && screen != PlayerScreen.Talk) BlurInput();
                shown = screen;
                Active(intro, screen == PlayerScreen.Intro);
                Active(explore, screen == PlayerScreen.Explore);
                Active(top, screen == PlayerScreen.Explore || screen == PlayerScreen.Talk || screen == PlayerScreen.Journal);
                Active(talk, screen == PlayerScreen.Talk);
                Active(book, screen == PlayerScreen.Journal);
                Active(modal, screen == PlayerScreen.Menu || screen == PlayerScreen.Error ||
                    screen == PlayerScreen.Timeout || screen == PlayerScreen.Wait);
                journal.Node.gameObject.SetActive(screen == PlayerScreen.Explore || screen == PlayerScreen.Talk);
                changedLayout = true;
            }
            Set(clockText, flow.ClockText);
            Set(clockCaption, screen == PlayerScreen.Talk ? "交谈中 · 时间暂停" :
                screen == PlayerScreen.Journal ? "阅读中 · 时间暂停" : openingPlace);

            if (screen == PlayerScreen.Explore)
            {
                string action = flow.Hint;
                if (action != hint.text) changedLayout = true;
                Set(hint, action);
                Active(hintPlate, !string.IsNullOrEmpty(action));
                Active(crosshair, true);
                string feedback = flow.Notice;
                if (feedback != notice.text) changedLayout = true;
                Set(notice, feedback);
                Active(noticePlate, !string.IsNullOrEmpty(feedback));
            }
            else if (screen == PlayerScreen.Talk)
            {
                Set(speaker, flow.Speaker);
                Set(line, flow.Line);
                Set(talkNotice, flow.Notice);
                if (previousSpeakerLine != flow.Line || previousTalkNotice != flow.Notice)
                {
                    resetReadingPosition = previousSpeakerLine != flow.Line;
                    previousSpeakerLine = flow.Line;
                    previousTalkNotice = flow.Notice;
                    changedLayout = true;
                }
                if (!input.isFocused && input.text != flow.Draft) input.SetTextWithoutNotify(flow.Draft ?? "");
                Set(placeholder, flow.HasDialogueService ? "想对林说什么？" : "预设对话 · 可提到镜头、隐瞒或同行");
                send.Button.interactable = flow.CanSend && !string.IsNullOrWhiteSpace(input.text);
                send.Label.color = send.Button.interactable ? Cream : new Color32(169, 150, 137, 255);
                UpdateChoices(talkChoices);
            }
            else if (screen == PlayerScreen.Journal && previousJournal != flow.JournalText)
            {
                previousJournal = flow.JournalText;
                var blocks = (previousJournal ?? "").Replace("\r","").Split(new[] { "\n\n" }, StringSplitOptions.None);
                for (int i = 0; i < sectionBody.Length; i++)
                {
                    string block = i < blocks.Length ? blocks[i] : "";
                    int newline = block.IndexOf('\n');
                    Set(sectionTitles[i], newline < 0 ? block : block.Substring(0, newline));
                    Set(sectionBody[i], newline < 0 ? "" : block.Substring(newline + 1));
                }
                changedLayout = true;
            }
            else if (screen == PlayerScreen.Menu || screen == PlayerScreen.Error ||
                     screen == PlayerScreen.Timeout || screen == PlayerScreen.Wait)
            {
                Set(modalTitle, screen == PlayerScreen.Menu ? (string.IsNullOrEmpty(flow.Line) ? "暂停" : "重新开始？") : screen == PlayerScreen.Timeout ?
                    "导演回来了" : screen == PlayerScreen.Wait ? "等待林的回复" : "林停下了");
                if (modalBody.text != flow.Line)
                {
                    changedLayout = true;
                    if (screen == PlayerScreen.Menu && EventSystem.current)
                        EventSystem.current.SetSelectedGameObject(modalChoices[0].Node.gameObject);
                }
                Set(modalBody, flow.Line);
                UpdateChoices(modalChoices);
            }
            var size = canvasRect.rect.size;
            if (changedLayout || lastSize != size)
            {
                lastSize = size;
                Layout(size.x, size.y);
                changedLayout = false;
            }
            if (transition) SelectDefault(screen);
            if (screen == PlayerScreen.Talk && readHint.gameObject.activeSelf)
            {
                float position = lineScroll.verticalNormalizedPosition;
                readEarlier.Button.interactable = position < .995f;
                readLater.Button.interactable = position > .005f;
                readEarlier.Label.color = readEarlier.Button.interactable ? Cream : Soft;
                readLater.Label.color = readLater.Button.interactable ? Cream : Soft;
            }
        }

        private void UpdateChoices(Control[] controls)
        {
            var choices = flow.Choices;
            int count = Mathf.Min(controls.Length, choices.Count);
            if (choiceCount != count)
            {
                choiceCount = count;
                changedLayout = true;
            }
            for (int i = 0; i < controls.Length; i++)
            {
                var control = controls[i];
                Active(control.Node, i < count);
                if (i >= count) continue;
                if (control.Label.text != choices[i].Label) changedLayout = true;
                Set(control.Label, choices[i].Label);
            }
        }

        private void SelectDefault(PlayerScreen screen)
        {
            if (!EventSystem.current) return;
            Selectable target = null;
            if (screen == PlayerScreen.Intro) target = start.Button;
            else if (screen == PlayerScreen.Talk) target = choiceCount > 0 ? talkChoices[0].Button : exitTalk.Button;
            else if (screen == PlayerScreen.Journal) target = closeBook.Button;
            else if (modal.gameObject.activeSelf && choiceCount > 0) target = modalChoices[0].Button;
            EventSystem.current.SetSelectedGameObject(target ? target.gameObject : null);
        }

        private void AdvanceTab(bool reverse)
        {
            if (!EventSystem.current || flow.Screen == PlayerScreen.Explore) return;
            // Sibling order is the reading order (e.g. choices, draft, send, leave).
            var controls = canvasRect.GetComponentsInChildren<Selectable>();
            int current = Array.FindIndex(controls, item => item.gameObject == EventSystem.current.currentSelectedGameObject);
            for (int step = 1; step <= controls.Length; step++)
            {
                int index = (current + (reverse ? -step : step) + 2 * controls.Length) % controls.Length;
                if (!controls[index].IsInteractable()) continue;
                controls[index].Select();
                return;
            }
        }

        private void Choose(int index)
        {
            var choices = flow.Choices;
            if (index >= choices.Count) return;
            flow.Choose(choices[index].Id);
            Refresh();
        }

        private void SendDraft()
        {
            if (submitFrame == Time.frameCount || flow.Screen != PlayerScreen.Talk || !flow.CanSend ||
                string.IsNullOrWhiteSpace(input.text) || !string.IsNullOrEmpty(Input.compositionString) ||
                Time.frameCount - lastCompositionFrame <= 1) return;
            submitFrame = Time.frameCount;
            flow.Send(input.text);
            input.SetTextWithoutNotify(flow.Draft ?? "");
            Refresh();
        }

        private static void Active(RectTransform element, bool on)
        {
            if (element.gameObject.activeSelf != on) element.gameObject.SetActive(on);
        }

        private static void Set(Text field, string value)
        {
            value = value ?? "";
            if (field.text != value) field.text = value;
        }
    }
}
