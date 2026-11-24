#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace Before1800.PartB.Tests
{
    public sealed class ExperienceTests
    {
        PartBScene scene;
        [UnitySetUp] public IEnumerator Open()
        {
            GameLanguage.Change("zh");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Before1800Home/PartB_Residence.unity",new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;scene=Object.FindObjectOfType<PartBScene>();scene.GetComponent<PartCDialogue>().SetLive(false);
        }
        [TearDown] public void LanguageReset(){GameLanguage.Change("zh");}
        [UnityTest] public IEnumerator PrologueExplainsRoleBeforeClockStarts()
        {
            var flow=scene.GetComponent<GreyboxHUD>().Flow;
            for(int page=0;page<3;page++){flow.Begin();yield return null;Assert.AreEqual(PlayerScreen.Intro,flow.Screen);Assert.IsTrue(scene.State.IsPaused);}
            flow.Begin();Assert.AreEqual(PlayerScreen.Explore,flow.Screen);Assert.IsFalse(scene.State.IsPaused);
        }
        [UnityTest] public IEnumerator EnglishSwitchUpdatesVisibleOpeningAndSettings()
        {
            GameLanguage.Change("en");yield return null;yield return null;
            var view=scene.GetComponent<PlayerUIView>();
            Assert.IsTrue(view.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("Begin the visit")));
            view.OpenSettings();yield return null;
            Assert.IsTrue(view.SettingsOpen);Assert.IsTrue(view.GetComponentsInChildren<Text>().Any(t=>t.text=="Music"));
            GameLanguage.Change("zh");yield return null;yield return null;
            Assert.IsTrue(view.GetComponentsInChildren<Text>().Any(t=>t.text=="背景音乐"));view.CloseSettings();
        }
        [Test] public void EverydayObjectsHaveDistinctMemoriesAndReachableTargets()
        {
            var daily=scene.GetComponent<ResidenceMemories>();var places=daily.Places.ToArray();
            Assert.AreEqual(10,places.Length,"Every authored everyday place should be reachable: "+string.Join(",",places.Select(p=>p.Id)));
            foreach(var p in places){Assert.IsTrue(SessionState.IsStoryTarget(p.Id),p.Id);Assert.Greater(p.Memory.Length,50);}
            Assert.IsTrue(scene.World.CanInspect("memory_sofa"));
            Assert.IsTrue(scene.World.CanInspect("memory_books"));
            Assert.AreEqual(10,daily.Knowledge.Distinct().Count());
        }
        [UnityTest] public IEnumerator TitleMusicAndInteractionSoundAreAudible()
        {
            var mix=scene.GetComponent<ResidenceAudio>();
            var oldMaster=mix.MasterVolume;var oldMusic=mix.MusicVolume;var oldMute=mix.Muted;
            try
            {
                mix.Muted=false;mix.MasterVolume=.8f;mix.MusicVolume=.55f;
                yield return new WaitForSecondsRealtime(2);
                var music=scene.GetComponentsInChildren<AudioSource>().Single(a=>a.gameObject.name=="Letters — felt keys");
                Assert.IsTrue(music.isPlaying,"The title must actually start its music.");
                var samples=new float[music.clip.samples*music.clip.channels];music.clip.GetData(samples,0);
                double power=0;foreach(var sample in samples)power+=sample*sample;
                Assert.Greater(System.Math.Sqrt(power/samples.Length)*music.volume,.002,"The default music mix must have audible signal, not just a playing flag.");
                mix.PlayCue("interact");yield return null;
                var foley=scene.GetComponentsInChildren<AudioSource>().Single(a=>a.gameObject.name=="Small interactions");
                Assert.Greater(foley.volume,.2f);Assert.IsTrue(foley.isPlaying,"Interaction must trigger a sound.");
            }
            finally{mix.MasterVolume=oldMaster;mix.MusicVolume=oldMusic;mix.Muted=oldMute;}
        }
        [UnityTest] public IEnumerator ResidenceTargetsCanBeReachedWithPhysicalController()
        {
            var failures=new System.Collections.Generic.List<string>();
            foreach(var target in new[]{"sealed_letter","parent_reply","parents_diary","friend_postcard","memory_sofa","memory_books",SessionState.CameraTarget,SessionState.BroadcastTarget,SessionState.SetBackTarget})
            {
                scene.World.ResetWorld();
                var player=scene.World.Player.GetComponent<CharacterController>();player.enabled=false;player.transform.position=new Vector3(30,0,30);player.enabled=true;
                Physics.SyncTransforms();yield return null;
                TravelResult result=TravelResult.Moving;
                for(int step=0;step<650 && result==TravelResult.Moving;step++)result=scene.World.StepInspect(target,.05f);
                if(result!=TravelResult.Arrived)failures.Add(target+": "+scene.World.FailureCode+" at "+scene.World.Lin.position);
            }
            Assert.IsEmpty(failures,string.Join("; ",failures));
        }
        [UnityTest] public IEnumerator LinWaitsForPlayerThenResumesWithoutFailure()
        {
            scene.World.ResetWorld();
            var player=scene.World.Player.GetComponent<CharacterController>();
            var direction=scene.World.ObservationPoint.position-scene.World.Lin.position;direction.y=0;
            player.enabled=false;player.transform.position=scene.World.Lin.position+direction.normalized*.65f;player.enabled=true;
            Physics.SyncTransforms();yield return null;
            for(int i=0;i<120;i++)Assert.AreEqual(TravelResult.Moving,scene.World.StepInspect(SessionState.CameraTarget,.05f));
            Assert.IsTrue(scene.World.YieldingToPlayer);Assert.IsEmpty(scene.World.FailureCode);
            player.enabled=false;player.transform.position=new Vector3(30,0,30);player.enabled=true;Physics.SyncTransforms();
            TravelResult result=TravelResult.Moving;
            for(int i=0;i<650 && result==TravelResult.Moving;i++)result=scene.World.StepInspect(SessionState.CameraTarget,.05f);
            Assert.AreEqual(TravelResult.Arrived,result,scene.World.FailureCode);
        }
        [UnityTest] public IEnumerator ComparingLettersExplainsContradictionWithoutInventingProof()
        {
            scene.ToggleIntroOff();scene.Story.OpenLetterComparison();yield return null;
            Assert.IsTrue(scene.Story.LetterComparison);Assert.IsTrue(scene.State.IsPaused);
            Assert.IsEmpty(scene.State.VerifiedEvidence);Assert.IsFalse(scene.State.IsConcernResolved("C2"));
            Assert.IsFalse(scene.Story.HasPlayerRead("sealed_letter"));
            var texts=scene.GetComponent<PlayerUIView>().GetComponentsInChildren<Text>();
            Assert.IsTrue(texts.Any(t=>t.text.Contains("原信还封在家里")));
            scene.GetComponent<GreyboxHUD>().Flow.Choose("answer_letters");
            Assert.AreEqual(PlayerScreen.Talk,scene.GetComponent<GreyboxHUD>().Flow.Screen);
        }
        [UnityTest] public IEnumerator AudioDeviceResetRestoresRealOutput()
        {
            var mix=scene.GetComponent<ResidenceAudio>();mix.Muted=false;mix.MasterVolume=.8f;mix.MusicVolume=.55f;
            yield return new WaitForSecondsRealtime(1);
            AudioSettings.Reset(AudioSettings.GetConfiguration());
            yield return new WaitForSecondsRealtime(2);
            Assert.Greater(mix.RecoveryCount,0);Assert.IsTrue(mix.MusicPlaying);
            float[] samples=new float[2048];double max=0;
            for(int frame=0;frame<15;frame++) { AudioListener.GetOutputData(samples,0);double power=0;foreach(float v in samples)power+=v*v;max=System.Math.Max(max,System.Math.Sqrt(power/samples.Length));yield return new WaitForSecondsRealtime(.1f); }
            Assert.Greater(max,.008,"The listener must output actual music after device reset, not just have a playing flag.");
            mix.PlayCue("paper");yield return null;
            Assert.IsTrue(scene.GetComponentsInChildren<AudioSource>().Single(a=>a.gameObject.name=="Small interactions").isPlaying);
        }
        [Test] public void PlayerLabelsHidePrototypeIdentifiers()
        {
            Assert.IsFalse(GameLanguage.T("E4 已确认。电视跟着林想看的节目切换了。").Contains("E4"));
            Assert.IsFalse(GameLanguage.T("[E] 前门 · 告别 / 同行").Contains("[E]"));
            Assert.IsTrue(scene.World.Lin.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.sharedMesh && r.sharedMesh.GetBlendShapeIndex("A")>=0),"A real mouth shape must exist for speech.");
        }
        [UnityTest] public IEnumerator EndingVisitRequiresAnExplicitSecondChoice()
        {
            scene.ToggleIntroOff();
            var flow=scene.GetComponent<GreyboxHUD>().Flow;
            flow.ToggleMenu();flow.Choose("finish_visit");yield return null;
            Assert.IsFalse(scene.State.IsTerminal,"Opening the end-visit choice must not discard the run.");
            Assert.AreEqual("resume",flow.Choices[0].Id,"Return is the safe default.");
            flow.Back();Assert.AreEqual(PlayerScreen.Menu,flow.Screen);
            flow.Choose("finish_visit");flow.Choose("finish_visit_yes");
            Assert.IsTrue(scene.State.IsTerminal);
        }
        [UnityTest] public IEnumerator LettersAreNotBuriedInDecorativeTableObjects()
        {
            yield return null;
            foreach(var id in new[]{"sealed_letter","parent_reply","friend_postcard"})
            {
                var prop=scene.Story.PaperFocus(id);
                var renderer=prop.GetComponent<Renderer>();
                foreach(var other in Object.FindObjectsOfType<MeshRenderer>())
                {
                    if(other==renderer || other.transform.IsChildOf(prop) || !other.enabled || !other.bounds.Contains(prop.position+Vector3.up*.018f))continue;
                    var mesh=other.GetComponent<MeshFilter>();if(!mesh || !mesh.sharedMesh)continue;
                    // The room is one combined mesh. Bounds alone cannot establish occlusion.
                    var probe=new GameObject("Temporary visual occlusion probe");
                    try
                    {
                        probe.transform.SetParent(other.transform,false);
                        var collider=probe.AddComponent<MeshCollider>();collider.sharedMesh=mesh.sharedMesh;
                        Physics.SyncTransforms();
                        var ray=new Ray(prop.position+Vector3.up*.5f,Vector3.down);
                        bool blocked=collider.Raycast(ray,out var hit,.48f);
                        Debug.Log("LETTER_VISIBLE_AUDIT "+id+" at "+prop.position+" blocked="+blocked+" surface="+hit.point);
                        Assert.IsFalse(blocked,"Letter is below visible furniture: "+id+" surface at "+hit.point);
                    }
                    finally{Object.DestroyImmediate(probe);}
                }
            }
        }
        [UnityTest] public IEnumerator SuccessfulVoicePreviewReleasesNormalDialogue()
        {
            var voice=scene.GetComponent<LinVoice>();
            // A finished preview leaves an available voice and a stopped source. This previously
            // kept preview=true forever, silently suppressing all subsequent dialogue speech.
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(LinVoice).GetField("preview",flags).SetValue(voice,true);
            yield return null;yield return null;
            Assert.IsFalse(voice.Previewing,"A successful preview must return control to ordinary dialogue.");
        }
        [UnityTest] public IEnumerator FailedOrCancelledOpeningAnswerDoesNotAdvanceTheGuide()
        {
            scene.ToggleIntroOff();scene.Story.OpenLetterComparison();
            var flow=scene.GetComponent<GreyboxHUD>().Flow;flow.Choose("answer_letters");
            ModelRequest pending=null;
            scene.PlayerLineRequested+=(request,_)=>pending=request;
            flow.Send("回信是我写的。");
            Assert.IsNotNull(pending);Assert.IsFalse(scene.Story.FirstAnswerSent);
            scene.TryCancelModelRequest(pending);flow.Tick();
            Assert.IsFalse(scene.Story.FirstAnswerSent);Assert.IsNotEmpty(flow.Draft);
            flow.Send("回信是我写的。");
            Assert.IsTrue(scene.TryReceivePlayerReply(pending,"你从什么时候开始的？","reply",null,out _));
            flow.Tick();yield return null;
            Assert.IsTrue(scene.Story.FirstAnswerSent);
        }
        [UnityTest] public IEnumerator ConversationTurnsLinTowardPlayerWithoutMovingOrAdvancingTime()
        {
            scene.ToggleIntroOff();scene.Story.OpenLetterComparison();
            scene.GetComponent<GreyboxHUD>().Flow.Choose("answer_letters");
            var lin=scene.World.Lin;
            var toward=Vector3.ProjectOnPlane(scene.World.Player.position-lin.position,Vector3.up).normalized;
            lin.rotation=Quaternion.LookRotation(-toward);
            var position=lin.position;var elapsed=scene.State.ElapsedGameSeconds;
            yield return new WaitForSecondsRealtime(3);
            Assert.Less(Vector3.Angle(lin.forward,toward),5f,"Lin must face the speaker, including when dialogue pauses the clock.");
            Assert.Less(Vector3.Distance(position,lin.position),.001f);
            Assert.AreEqual(elapsed,scene.State.ElapsedGameSeconds);
        }
        [UnityTest] public IEnumerator FoyerCatchIsVisibleAndDeparturePromptCannotStealIt()
        {
            scene.ToggleIntroOff();scene.AcceptHardwareInput=false;
            var player=scene.World.Player.GetComponent<CharacterController>();
            player.enabled=false;player.transform.position=new Vector3(.85f,.02f,3.05f);player.enabled=true;
            var camera=scene.World.ViewCamera.transform;var catchPoint=scene.World.Station.DoorControl;
            camera.LookAt(catchPoint.position);Physics.SyncTransforms();yield return null;
            AssertMeshVisibility(camera.position,catchPoint);
            scene.GetComponent<GreyboxHUD>().Flow.Use();
            Assert.IsTrue(scene.World.Station.DoorOpen,"An unobstructed foyer catch must open, even near the front door.");
            AssertMeshVisibility(camera.position,catchPoint);
            Assert.IsTrue(scene.World.Station.CanStartObservation(camera),"The open panel must leave its catch usable from the same foyer position.");
            var eyes=scene.World.Station.ObservationPoint.position+Vector3.up*1.55f;
            AssertMeshVisibility(eyes,scene.World.Station.SetBrace);
            AssertMeshVisibility(eyes,scene.World.Station.SetMark);
            Assert.IsFalse(scene.State.E5Verified);Assert.IsFalse(scene.State.RouteConfirmed);
        }
        [Test] public void ReopeningConversationDoesNotRepeatTheAnsweredLetterQuestion()
        {
            scene.ToggleIntroOff();scene.Story.OpenLetterComparison();
            var flow=scene.GetComponent<GreyboxHUD>().Flow;flow.Choose("answer_letters");
            scene.GetComponent<PartCDialogue>().SetLive(true);
            StringAssert.Contains("蓝杯子",flow.Line);
            scene.Story.FirstAnswerSent=true;
            StringAssert.DoesNotContain("蓝杯子",flow.Line);
            StringAssert.Contains("刚才的话",flow.Line);
            scene.GetComponent<PartCDialogue>().SetLive(false);
        }
        static void AssertMeshVisibility(Vector3 origin,Transform target)
        {
            var ray=new Ray(origin,target.position-origin);
            float distance=Vector3.Distance(origin,target.position);
            foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                var mesh=renderer.GetComponent<MeshFilter>();
                if(!renderer.enabled || !mesh || !mesh.sharedMesh || renderer.transform.IsChildOf(target))continue;
                var probe=new GameObject("Catch visibility test");
                try
                {
                    probe.transform.SetParent(renderer.transform,false);
                    var collider=probe.AddComponent<MeshCollider>();collider.sharedMesh=mesh.sharedMesh;Physics.SyncTransforms();
                    Assert.IsFalse(collider.Raycast(ray,out var hit,distance-.04f),target.name+" hidden by "+renderer.name+" at "+hit.point);
                }
                finally{Object.DestroyImmediate(probe);}
            }
        }
    }
}
#endif
