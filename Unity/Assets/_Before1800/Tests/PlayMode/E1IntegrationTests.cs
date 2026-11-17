using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Before1800.Tests
{
    public class E1IntegrationTests
    {
        private PrototypeController controller;
        [UnitySetUp] public IEnumerator Setup()
        {
            Time.timeScale=1;
            yield return SceneManager.LoadSceneAsync("Prototype_E1");
            yield return null;
            controller=UnityEngine.Object.FindFirstObjectByType<PrototypeController>();
            Assert.NotNull(controller);
            controller.Client.serviceUrl="http://127.0.0.1:8001";
            controller.Begin();
        }
        [UnityTearDown] public IEnumerator Cleanup(){Time.timeScale=1;yield return null;}
        private IEnumerator Until(Func<bool> condition,float timeout,string label)
        {
            float start=Time.realtimeSinceStartup;
            while(!condition() && Time.realtimeSinceStartup-start<timeout)yield return null;
            Assert.IsTrue(condition(),label+"; phase="+controller.cameraExperiment.Phase+"; trace="+controller.Trace.RecentText);
        }
        [UnityTest] public IEnumerator FixtureTwoRoundsActuallyObserveAndFeedBack()
        {
            controller.DiscoverCamera();Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            controller.OpenDialogue();controller.View.Input.text="Please inspect the camera.";controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,15,"fixture request finished");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            Assert.AreEqual("inspect",controller.State.PendingDecision.action);
            Assert.IsFalse(controller.cameraExperiment.Running);
            float frozen=controller.State.Snapshot.elapsed_game_seconds;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.AreEqual(frozen,controller.State.Snapshot.elapsed_game_seconds);
            controller.ContinueDecision();
            yield return Until(()=>controller.State.HasActiveAction,2,"action started");
            controller.OpenMenu();Vector3 position=controller.lin.transform.position;
            yield return new WaitForSecondsRealtime(.35f);
            Assert.Less(Vector3.Distance(position,controller.lin.transform.position),.02f);
            controller.CloseMenu();
            yield return Until(()=>!controller.State.HasActiveAction,35,"observation completed");
            Assert.Contains("E1",controller.State.Snapshot.verified_evidence);
            Assert.AreEqual(2,controller.cameraExperiment.ObservedPositions);
            Assert.AreEqual("open",controller.State.Snapshot.concerns.C1);
            Assert.IsFalse(controller.State.Snapshot.escape_agreed);
            controller.player.ResetPosition(controller.lin.transform.position+Vector3.back*1.5f,0);
            controller.OpenDialogue();controller.View.Input.text="What did you observe?";controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,15,"second fixture request finished");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            Assert.AreEqual("reply",controller.State.PendingDecision.action);
            StringAssert.Contains("TEST_FIXTURE",controller.LastRawResponse);
            StringAssert.Contains("track me",controller.State.PendingDecision.dialogue);
            controller.ContinueDecision();
            Assert.AreEqual(1,controller.State.Snapshot.verified_evidence.Length);
        }
        [UnityTest] public IEnumerator KeyboardEventsOpenDialogueMenuAndTrace()
        {
            var oldFocus=InputSystem.settings.backgroundBehavior;
            var oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard=InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.T));
                yield return null; yield return null;
                Assert.IsTrue(controller.View.DialogueVisible,"T route: current="+Keyboard.current?.deviceId+" expected="+keyboard.deviceId+" down="+keyboard.tKey.isPressed+" mode="+InputSystem.settings.updateMode+" paused="+controller.Pauses.IsPaused+" distance="+Vector3.Distance(controller.player.transform.position,controller.lin.transform.position));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));
                yield return null; yield return null;
                Assert.IsTrue(controller.View.MenuVisible,"Escape opens menu while dialogue stays paused");
                InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F3));
                yield return null; yield return null;
                Assert.IsTrue(controller.View.DebugVisible,"F3 opens trace");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.backgroundBehavior=oldFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;
            }
        }
        [UnityTest] public IEnumerator CameraIsDiscoverableThroughActualRaycast()
        {
            controller.player.inputEnabled=false;
            controller.player.ResetPosition(new Vector3(0,.02f,2.2f),0);
            controller.player.viewCamera.transform.LookAt(controller.cameraExperiment.cameraPivot.position);
            Physics.SyncTransforms(); controller.Interact();
            Assert.Contains("E1",controller.State.Snapshot.player_discovered);
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            yield return null;
        }
        [UnityTest] public IEnumerator OfflineServiceCanRecoverWithoutChangingFacts()
        {
            controller.DiscoverCamera(); controller.OpenDialogue();
            controller.Client.serviceUrl="http://127.0.0.1:1";
            controller.Client.timeoutSeconds=1;
            controller.View.Input.text="Inspect the camera."; controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,5,"offline request terminated");
            Assert.IsFalse(controller.State.HasPendingDecision);
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            Assert.IsTrue(controller.Pauses.IsPaused);
            controller.Client.serviceUrl="http://127.0.0.1:8001";
            controller.Client.timeoutSeconds=15; controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,20,"retry returned");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
        }
        [UnityTest] public IEnumerator OverlappingPointsNeverGrantEvidence()
        {
            controller.cameraExperiment.pointB.position=controller.cameraExperiment.pointA.position;
            controller.DiscoverCamera(); controller.OpenDialogue();
            controller.View.Input.text="Inspect the camera."; controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,15,"fixture returned");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            controller.ContinueDecision(); yield return null;
            yield return Until(()=>!controller.State.HasActiveAction,5,"invalid experiment ended");
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            Assert.AreEqual("failed",controller.State.Snapshot.last_action_result.status);
        }
        [UnityTest] public IEnumerator StationaryLensNeverGrantsEvidence()
        {
            controller.cameraExperiment.trackingEnabled=false;
            controller.cameraExperiment.cameraPivot.rotation=Quaternion.LookRotation(Vector3.up);
            controller.DiscoverCamera(); controller.OpenDialogue();
            controller.View.Input.text="Inspect the camera."; controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,15,"fixture returned");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            controller.ContinueDecision(); yield return null;
            yield return Until(()=>!controller.State.HasActiveAction,35,"stationary lens rejected");
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            Assert.AreEqual("failed",controller.State.Snapshot.last_action_result.status);
        }
        [UnityTest] public IEnumerator CancelAndRestartNeverGrantEvidence()
        {
            controller.DiscoverCamera();controller.OpenDialogue();controller.View.Input.text="Inspect the camera.";controller.SendFromInput();
            yield return Until(()=>!controller.Waiting,15,"fixture returned");
            Assert.IsTrue(controller.State.HasPendingDecision,controller.LastRawResponse);
            controller.ContinueDecision();yield return null;
            controller.OpenMenu();controller.CancelInspection();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            Assert.IsFalse(controller.State.HasActiveAction);
            string old=controller.State.Snapshot.session_id;controller.RestartSession();
            Assert.AreNotEqual(old,controller.State.Snapshot.session_id);
            Assert.IsEmpty(controller.State.Snapshot.verified_evidence);
            Assert.IsFalse(controller.Pauses.IsPaused);
        }
    }
}
