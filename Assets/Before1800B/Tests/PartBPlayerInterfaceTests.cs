#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Before1800.PartB.Tests
{
    public sealed class PartBPlayerInterfaceTests
    {
        private PartBScene scene;
        private PlayerInteraction flow;
        private PlayerUIView view;

        [UnitySetUp]
        public IEnumerator OpenResidence()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            scene = UnityEngine.Object.FindObjectOfType<PartBScene>();
            // Canvas mechanics are deterministic; live narrative decisions have separate validation.
            scene.GetComponent<PartCDialogue>().SetLive(false);
            flow = scene.GetComponent<GreyboxHUD>().Flow;
            view = scene.GetComponent<PlayerUIView>();
            Assert.AreEqual(PlayerScreen.Intro, flow.Screen);
            Assert.IsFalse(flow.DebugOpen);
            var first = EventSystem.current.currentSelectedGameObject;
            Assert.IsNotNull(first, "The intro needs a keyboard-accessible primary action.");
            for(int page=0;page<4;page++)
            { ExecuteEvents.Execute(first, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler); yield return null; }
            yield return null;
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
        }

        private void FaceLin()
        {
            var world = scene.World;
            world.ViewCamera.transform.LookAt(world.Lin.position + Vector3.up * 1.38f);
            Assert.IsTrue(world.CanTalkToLin, "Lin should be within conversational distance near the residence spawn.");
        }

        private Button VisibleChoice(string wording)
        {
            var button = view.GetComponentsInChildren<Button>(false).FirstOrDefault(item =>
                item.GetComponentsInChildren<Text>(false).Any(label => label.text.Contains(wording)));
            Assert.IsNotNull(button, "No visible button containing " + wording);
            Assert.IsTrue(button.IsInteractable(), "Visible choice should accept input.");
            return button;
        }

        [UnityTest]
        public IEnumerator WalkingStraightTowardsLinKeepsTheVisibleConversationPrompt()
        {
            var before = scene.World.Player.position;
            var direction = scene.World.Player.forward;
            Assert.IsFalse(scene.State.IsPaused);
            for (int i = 0; i < 14; i++) scene.StepSimulation(.02f, Vector2.up, Vector2.zero);
            yield return null;
            Assert.Less(Vector3.Distance(scene.World.Player.position, scene.World.Lin.position), 1.5f,
                "start=" + before + " forward=" + direction + " end=" + scene.World.Player.position + " Lin=" + scene.World.Lin.position + " screen=" + flow.Screen);
            Assert.IsTrue(scene.World.CanTalkToLin, "A player facing Lin from the spawn should still be able to talk after walking closer.");
            flow.Use();
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
        }

        [UnityTest]
        public IEnumerator ConversationRequiresVisibleLinAndJournalReturnsToTheSamePausedDialogue()
        {
            var world = scene.World;
            world.ViewCamera.transform.rotation = Quaternion.LookRotation(world.Player.position - world.Lin.position);
            flow.Use();
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            FaceLin();
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstruction.transform.position = Vector3.Lerp(world.ViewCamera.transform.position, world.Lin.position + Vector3.up * 1.38f, .5f);
            obstruction.transform.localScale = Vector3.one * .55f;
            Physics.SyncTransforms();
            try
            {
                flow.Use();
                Assert.AreEqual(PlayerScreen.Explore, flow.Screen, "A wall between the speakers must prevent conversation.");
            }
            finally { UnityEngine.Object.DestroyImmediate(obstruction); }
            Physics.SyncTransforms();
            flow.Use();
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            yield return null;
            var line = flow.Line;
            var elapsed = scene.State.ElapsedGameSeconds;
            flow.ToggleJournal();
            Assert.AreEqual(PlayerScreen.Journal, flow.Screen);
            scene.StepSimulation(4, Vector2.one, Vector2.one);
            flow.Back();
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.AreEqual(line, flow.Line);
            Assert.AreEqual(elapsed, scene.State.ElapsedGameSeconds);
            Assert.IsTrue(scene.State.IsPaused);
            flow.Back();
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            Assert.IsFalse(scene.State.IsPaused);
        }

        [UnityTest]
        public IEnumerator ChoosingTheLensAndConfirmingInTheCanvasResumesMovementWithoutGrantingEvidence()
        {
            FaceLin();
            flow.Use();
            yield return null;
            VisibleChoice("镜头").onClick.Invoke();
            yield return null;
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsNull(scene.State.Pending, "Talking about the lens must leave the player free to continue speaking.");
            VisibleChoice("一起去").onClick.Invoke();
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            for (int i = 0; i < 800 && scene.State.Stage != ActionStage.Arrived; i++)
                scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
            Assert.AreEqual(ActionStage.Arrived, scene.State.Stage, scene.Feedback);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            flow.ToggleJournal();
            Assert.AreEqual(PlayerScreen.Journal, flow.Screen);
            Assert.IsFalse(scene.State.E1Verified);
        }

        [UnityTest]
        public IEnumerator TextAndCancellationCannotGrantFactsOrDeliverAStaleServiceReply()
        {
            FaceLin();
            flow.Use();
            yield return null;
            const string message = "我想和你聊聊刚才看到的事。";
            flow.Draft = message;
            flow.Send(message);
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
            ModelRequest dispatched = null;
            int requests = 0;
            scene.PlayerLineRequested += (request, text) => { dispatched = request; requests++; Assert.AreEqual(message, text); };
            flow.Send(message);
            Assert.IsNotNull(dispatched);
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsTrue(flow.Waiting);
            Assert.IsFalse(flow.CanSend);
            flow.Send(message);
            Assert.AreEqual(1, requests);
            flow.Back();
            Assert.IsFalse(scene.TryReceivePlayerReply(dispatched, "我跟你去。", "follow", "player", out _));
            Assert.IsNull(scene.State.Pending);
            Assert.IsEmpty(scene.State.VerifiedEvidence);
        }

        [Test]
        public void ChineseFontAndOneActivePlayerCanvasArePresent()
        {
            var font = PlayerUIFont.Get();
            Assert.IsNotNull(font);
            foreach (char c in "林镜头顾虑现场手记") Assert.IsTrue(font.HasCharacter(c), c.ToString());
            Assert.AreEqual(1, view.GetComponentsInChildren<Canvas>(false).Length);
            scene.Restart();
            Assert.AreEqual(1, view.GetComponentsInChildren<Canvas>(false).Length, "Restart should reuse the player interface.");
            Assert.IsFalse(scene.GetComponent<GreyboxHUD>().Flow.DebugOpen);
        }

        [UnityTest]
        public IEnumerator LinCanFinishSpeakingAndReturnThePlayerToTheResidence()
        {
            const string question = "我想听你说。";
            const string ordinary = "你还记得那封信吗？";
            const string farewell = "把信留下，我想自己看看。";
            int requests = 0;
            scene.PlayerLineRequested += (request, text) =>
            {
                requests++;
                bool first = text == question;
                Assert.IsTrue(scene.TryReceivePlayerReply(request, first ? ordinary : farewell,
                    first ? "reply" : "end_conversation", null, out var reason), reason);
            };
            FaceLin();
            flow.Use();
            flow.Send(question);
            yield return null;
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.AreEqual(ordinary, flow.Line);
            Assert.IsNotNull(view.GetComponentInChildren<InputField>());
            Assert.IsFalse(flow.Closing);
            Assert.IsTrue(flow.CanSend);

            flow.Send("信在这里。");
            yield return null;
            yield return null;
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsTrue(flow.Closing);
            Assert.IsTrue(scene.State.IsPaused);
            Assert.AreEqual(farewell, flow.Line);
            Assert.IsNull(view.GetComponentInChildren<InputField>(), "Lin's closing line should have no reply prompt.");
            Assert.IsTrue(view.GetComponentsInChildren<Text>(false).Any(label => label.text.Contains(farewell)),
                "The player should see Lin's final line before control returns.");
            Assert.IsFalse(flow.CanSend);
            flow.Send("等一下。");
            Assert.AreEqual(2, requests);

            float deadline = Time.realtimeSinceStartup + 8f;
            while (flow.Screen == PlayerScreen.Talk && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            Assert.IsFalse(scene.State.IsPaused);
            FaceLin();
            flow.Use();
            yield return null;
            Assert.IsNotNull(view.GetComponentInChildren<InputField>());
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsFalse(flow.Closing);
            Assert.IsTrue(flow.CanSend);
        }

        [UnityTest]
        public IEnumerator AnAcceptedInvestigationShowsTheLineThenLinWalksToReadTheOriginal()
        {
            const string target = "sealed_letter";
            const string line = "我自己去看封口。";
            var adapter = scene.GetComponent<PartCDialogue>();
            scene.PlayerLineRequested += (ticket, text) =>
                ApplyDecision(adapter, ticket, adapter.BuildInput(ticket, text), line, "inspect", target);
            FaceLin();
            flow.Use();
            var initialPosition = scene.World.Lin.position;
            flow.Send("原信就在茶几上。");
            yield return null;
            yield return null;
            Assert.IsTrue(flow.Closing);
            Assert.IsTrue(view.GetComponentsInChildren<Text>(false).Any(label => label.text.Contains(line)));
            Assert.AreEqual(target, scene.State.Pending?.TargetId);
            Assert.IsNull(scene.State.Active);
            Assert.AreEqual(initialPosition, scene.World.Lin.position);
            Assert.IsFalse(scene.Story.HasLinRead(target));

            float deadline = Time.realtimeSinceStartup + 8f;
            while (flow.Screen == PlayerScreen.Talk && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            for (int i = 0; i < 800 && scene.State.Stage != ActionStage.Arrived; i++)
                scene.StepSimulation(.03f, Vector2.zero, Vector2.zero);
            Assert.AreEqual(ActionStage.Arrived, scene.State.Stage, scene.Feedback);
            Assert.IsTrue(scene.World.AtObservation(target));
            Assert.IsFalse(scene.Story.HasLinRead(target), "She must also spend time reading after arrival.");
            deadline = Time.realtimeSinceStartup + 5f;
            while (!scene.Story.HasLinRead(target) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(scene.Story.HasLinRead(target));
            Assert.IsEmpty(scene.State.VerifiedEvidence);
        }

        private static void ApplyDecision(PartCDialogue adapter, ModelRequest ticket, PartCDialogue.Input input,
            string line, string action, string target = null, string resolvedConcern = null)
        {
            var output = new PartCDialogue.Output {
                session_id = input.session_id, request_id = input.request_id, turn_id = input.turn_id,
                state_version = input.state_version, model = "test",
                decision = new PartCDialogue.Decision {
                    dialogue = line, action = action, target_id = target, concern_id = resolvedConcern,
                    concern_status = resolvedConcern == null ? "keep" : "resolved",
                    basis_turn_id = resolvedConcern == null ? null : input.turn_id
                }
            };
            adapter.ApplyGatewayResponse(ticket, input, JsonUtility.ToJson(output));
        }

        [Test]
        public void OneFocusedQuestionAtTheBreakCanLeadToAcknowledgingTrustAndClosingInTheSameTurn()
        {
            const string question = "那我写的时候，谁在看？";
            const string goodbye = "现在我知道了。让我自己想一会儿。";
            var adapter = scene.GetComponent<PartCDialogue>();
            PartCDialogue.Context context = null;
            scene.PlayerLineRequested += (ticket, text) => {
                var input = adapter.BuildInput(ticket, text);
                context = input.context;
                if (text == "回信是我写的。") ApplyDecision(adapter, ticket, input, question, "reply");
                else if (text == "我利用了你对父母的信任。你有权自己选。")
                    ApplyDecision(adapter, ticket, input, goodbye, "end_conversation", resolvedConcern: "C2");
                else ApplyDecision(adapter, ticket, input, "我听见了。", "reply");
            };
            FaceLin();
            flow.Use();
            for (int i = 0; i < PartCDialogue.PlainReplyLimit; i++)
            {
                flow.Send("我还在这里。");
                Assert.IsFalse(context.conversation_needs_break);
                Assert.IsFalse(context.allow_break_followup);
            }
            flow.Send("回信是我写的。");
            Assert.IsTrue(context.conversation_needs_break && context.allow_break_followup);
            Assert.AreEqual(question, flow.Line);
            Assert.IsFalse(flow.Closing);
            Assert.IsFalse(scene.State.IsConcernResolved("C2"));

            flow.Send("再说些别的。");
            Assert.IsTrue(context.conversation_needs_break);
            Assert.IsFalse(context.allow_break_followup);
            Assert.AreEqual(question, flow.Line, "A second ordinary reply at this break must be rejected.");
            Assert.IsTrue(flow.CanSend);

            flow.Send("我利用了你对父母的信任。你有权自己选。");
            Assert.IsTrue(context.allow_resolved_closing);
            Assert.AreEqual(goodbye, flow.Line);
            Assert.IsTrue(flow.Closing);
            Assert.IsTrue(scene.State.IsConcernResolved("C2"));
            Assert.IsFalse(scene.State.IsConcernResolved("C1"));
            Assert.IsFalse(scene.State.EscapeAgreed);
            Assert.IsNull(scene.State.Pending);
        }

        [UnityTest]
        public IEnumerator LeavingDuringTheFarewellDoesNotCloseTheNextConversation()
        {
            scene.PlayerLineRequested += (request, _) => Assert.IsTrue(
                scene.TryReceivePlayerReply(request, "让我想想。", "end_conversation", null, out var reason), reason);
            FaceLin();
            flow.Use();
            flow.Send("你愿意说说吗？");
            yield return null;
            Assert.IsTrue(flow.Closing);
            flow.Back();
            Assert.AreEqual(PlayerScreen.Explore, flow.Screen);
            FaceLin();
            flow.Use();
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            yield return new WaitForSecondsRealtime(4f);
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.IsFalse(flow.Closing);
            Assert.IsTrue(flow.CanSend);
        }

        [UnityTest]
        public IEnumerator LongReplyCanBeReadInSectionsWhileTimeAndActionsStayPaused()
        {
            FaceLin();
            flow.Use();
            string fullReply = string.Concat(Enumerable.Repeat("我记得那天电视的位置。你能陪我再看一眼吗？", 22));
            scene.PlayerLineRequested += (request, _) =>
                Assert.IsTrue(scene.TryReceivePlayerReply(request, fullReply, "reply", null, out var reason), reason);
            flow.Send("你记得什么？");
            yield return null;
            yield return null;
            Assert.AreEqual(PlayerScreen.Talk, flow.Screen);
            Assert.AreEqual(fullReply, flow.Line);
            var scroll = view.GetComponentInChildren<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.Greater(scroll.content.rect.height, scroll.viewport.rect.height * 2);
            var before = scroll.verticalNormalizedPosition;
            VisibleChoice("下一段").onClick.Invoke();
            yield return null;
            Assert.Less(scroll.verticalNormalizedPosition, before);
            Assert.IsNotNull(VisibleChoice("结束交谈"));
            Assert.IsTrue(scene.State.IsPaused);
            Assert.IsNull(scene.State.Pending);
        }
    }
}
#endif
