using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Before1800
{
    public sealed class PrototypeController : MonoBehaviour
    {
        public FirstPersonPlayer player;
        public LinMotor lin;
        public CameraExperiment cameraExperiment;
        public Transform playerSpawn;
        public Transform linSpawn;
        public float gameSecondsPerRealSecond = 5;
        public GameStateService State { get; private set; }
        public PauseService Pauses { get; private set; }
        public PrototypeView View { get; private set; }
        public LlmClient Client { get; private set; }
        public TraceRecorder Trace { get; private set; }
        public bool Waiting { get; private set; }
        public string LastRawResponse { get; private set; }
        private readonly List<DialogueEntry> history = new List<DialogueEntry>();
        private IDisposable dialoguePause, menuPause, introPause, terminalPause;
        private DecisionRequest currentRequest;
        private Coroutine requestRoutine, actionRoutine;
        private string lastInput = "";
        private bool started;
        private float refreshAt;

        private void Awake()
        {
            State = new GameStateService(); Pauses = new PauseService(); Trace = new TraceRecorder();
            Client = GetComponent<LlmClient>() ?? gameObject.AddComponent<LlmClient>();
            Client.Configure();
            View = GetComponent<PrototypeView>() ?? gameObject.AddComponent<PrototypeView>(); View.Build();
            lin.IsPaused = () => Pauses.IsPaused;
            cameraExperiment.IsPaused = () => Pauses.IsPaused;
            Pauses.Changed += paused => { Time.timeScale = paused ? 0 : 1; UpdateCursor(); };
            State.Changed += snapshot => Trace.Record("state",snapshot,"v"+snapshot.state_version+" facts=["+string.Join(",",snapshot.verified_evidence)+"]");
            cameraExperiment.PhaseChanged += phase => { Trace.Record("experiment",new { phase, instance_id=cameraExperiment.InstanceId },phase); View.ShowToast(phase); };
            View.OnBegin = Begin; View.OnSend = SendFromInput; View.OnContinue = ContinueDecision;
            View.OnCancel = CloseDialogue; View.OnRetry = () => { View.Input.text=lastInput; SendFromInput(); };
            View.OnQuickInput = value => { if (!Waiting && !State.HasPendingDecision) View.Input.text=value; };
            View.OnResume = CloseMenu; View.OnRestart = RestartSession; View.OnQuit = () => Application.Quit();
            View.OnCancelInspection = CancelInspection;
            introPause = Pauses.Acquire("introduction");
            Trace.Record("session_started",new { session_id=State.Snapshot.session_id, service_url=Client.serviceUrl, build="E1-M1" },"Live API required; no automatic mock fallback");
            StartCoroutine(CheckService());
            if(Environment.GetCommandLineArgs().Contains("--fixture-smoke")) gameObject.AddComponent<PrototypeSmokeRunner>();
        }

        private void Update()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.f3Key.wasPressedThisFrame) View.ToggleDebug();
                if (Keyboard.current.escapeKey.wasPressedThisFrame && !View.IntroVisible)
                { if (View.MenuVisible) CloseMenu(); else OpenMenu(); }
                if (!Pauses.IsPaused && started)
                {
                    if (Keyboard.current.eKey.wasPressedThisFrame) Interact();
                    if (Keyboard.current.tKey.wasPressedThisFrame) OpenDialogue();
                }
            }
            player.inputEnabled = started && !Pauses.IsPaused;
            if (!Pauses.IsPaused && started) State.AdvanceGameTime(Time.deltaTime * gameSecondsPerRealSecond);
            if (State.Snapshot.phase != "playing" && terminalPause == null)
            {
                terminalPause = Pauses.Acquire("session_finished");
                AbortCurrentRequest();
                if (actionRoutine != null) StopCoroutine(actionRoutine);
                lin.Stop("Time is up");
                View.ShowToast("18:00 — the director has returned. Restart to try again.");
                OpenMenu();
            }
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .12f;
                View.Refresh(State.Snapshot,Pauses.IsPaused,cameraExperiment.Phase,Trace.RecentText);
                if (!Pauses.IsPaused) RefreshInteractionHint();
            }
        }

        public void Begin()
        {
            started=true; View.ShowIntro(false); introPause?.Dispose(); introPause=null; UpdateCursor();
        }

        private IEnumerator CheckService()
        {
            yield return Client.Health((raw,ok) =>
            {
                try
                {
                    var health=JObject.Parse(raw);
                    bool ready=health.Value<bool?>("ready")??false;
                    string provider=health.Value<string>("provider")??"API";
                    string model=health.Value<string>("model")??"unconfigured";
                    View.SetService(ready ? provider+" / "+model : "API not configured — edit service/.env",ready);
                    Trace.Record("health",health,ready?"Configuration ready (not proof of live inference)":"Missing API configuration");
                }
                catch { View.SetService("API offline — start the local service",false); }
            });
        }

        private InteractionTarget CurrentTarget()
        {
            var ray=new Ray(player.viewCamera.transform.position,player.viewCamera.transform.forward);
            if (Physics.Raycast(ray,out var hit,3.5f,~0,QueryTriggerInteraction.Ignore))
            {
                var target=hit.collider.GetComponentInParent<InteractionTarget>();
                if(target!=null && hit.distance<=target.range) return target;
            }
            return null;
        }

        private void RefreshInteractionHint()
        {
            if (State.HasActiveAction) { View.SetHint(cameraExperiment.Phase + "   |   Esc pauses the experiment"); return; }
            var target=CurrentTarget();
            View.SetHint(target==null?"WASD  Move     Mouse  Look     E  Interact": "E  "+target.displayName);
        }

        public void Interact()
        {
            if (State.HasActiveAction) return;
            var target=CurrentTarget(); if(target==null)return;
            if(target.targetId=="lin")OpenDialogue();
            else if(target.targetId=="camera_01") DiscoverCamera();
        }

        public void DiscoverCamera()
        {
            if(State.TryRecordDiscovery(State.Snapshot.session_id,Guid.NewGuid().ToString("N"),"E1",out var reason))
            {
                Trace.Record("discovery",new { evidence_id="E1",verified=false },"Player found the camera; Lin has not verified it");
                View.ShowToast("Camera found. Talk to Lin and invite him to inspect it.");
            }
            else View.ShowToast(State.Snapshot.verified_evidence.Contains("E1")?"Lin has already verified the tracking camera.":"You found it. Lin still needs to see what it does.");
        }

        public void OpenDialogue()
        {
            if(State.HasActiveAction) { View.ShowToast("Let Lin finish, or use Esc > Cancel inspection."); return; }
            if(Vector3.Distance(player.transform.position,lin.transform.position)>4.5f) { View.ShowToast("Move closer to Lin to talk."); return; }
            if(State.Snapshot.phase!="playing" || View.DialogueVisible)return;
            dialoguePause=Pauses.Acquire("dialogue"); View.ShowDialogue(true);
            View.SetConversation("Speak to Lin in your own words. His response will use only the facts he has actually verified.","WORLD PAUSED / awaiting your input",false,false,false);
            UpdateCursor();
        }

        public void SendFromInput()
        {
            if(Waiting || State.HasPendingDecision || !View.DialogueVisible)return;
            string input=View.Input.text.Trim();
            if(input.Length==0){View.ShowToast("Write a message first.");return;}
            AbortCurrentRequest();
            lastInput=input;
            var targets=new List<string>{"player"};
            var actions=new List<string>{"reply","follow"};
            if(State.Snapshot.player_discovered.Contains("E1") && CanReach(cameraExperiment.pointA.position) && CanReach(cameraExperiment.pointB.position))
            {targets.Add("camera_01");actions.Add("inspect");}
            currentRequest=State.CreateRequest(input,targets.ToArray(),actions.ToArray(),"user",history.TakeLast(8).ToArray());
            currentRequest.context.observations = State.Snapshot.verified_evidence.Contains("E1")
                ? new[]{"Lin personally observed camera_01 turn to track him at two separated positions. This verifies monitoring, not the entire explanation of the world."}
                : new[]{"Lin has not completed the camera observation experiment. A player's claim is not verified evidence."};
            if(!State.TryBeginRequest(currentRequest,out var reason)) { ShowError("Cannot start this request: "+reason);return; }
            Waiting=true;
            Trace.Record("request",currentRequest,"id="+currentRequest.request_id.Substring(0,8)+" facts=["+string.Join(",",currentRequest.context.verified_evidence)+"] input="+input);
            View.SetConversation("You: "+input,"WAITING FOR API / world remains paused",true,false,false);
            var submitted=currentRequest;
            requestRoutine=StartCoroutine(Client.Decide(submitted,(raw,error)=>HandleResponse(submitted,raw,error)));
        }

        private void HandleResponse(DecisionRequest submitted,string raw,string error)
        {
            if(currentRequest==null || currentRequest.request_id!=submitted.request_id || State.Snapshot.session_id!=submitted.session_id)
            {Trace.Record("late_response_ignored",new {submitted.request_id},"No state changes");return;}
            Waiting=false; requestRoutine=null; LastRawResponse=raw;
            Trace.Record("raw_response",new {submitted.request_id,raw,error},error??"Received model envelope");
            if(error!=null)
            {
                State.CancelRequest(submitted.request_id);
                string message=error;
                try {var detail=JObject.Parse(raw);message=detail["error"]?["message"]?.ToString()??detail["detail"]?.ToString()??message;}catch{}
                if(message.Contains("config",StringComparison.OrdinalIgnoreCase)||message.Contains("key",StringComparison.OrdinalIgnoreCase))
                    message="API configuration is missing. Add the base URL, model and key in service/.env, then restart the service. Your game state has not changed.";
                ShowError(message);return;
            }
            if(!State.TryStageResponse(raw,out var reason))
            {State.CancelRequest(submitted.request_id);ShowError("The reply was rejected: "+reason+". No action was executed.");return;}
            var decision=State.PendingDecision;
            Trace.Record("validated",decision,"Pending "+decision.action+"; waiting for Continue");
            View.SetConversation("You: "+submitted.player_input+"\n\nLin: "+decision.dialogue,"REPLY RECEIVED / Continue to resume the world",false,true,false);
            try
            {
                var envelope=JObject.Parse(raw);
                View.SetService(envelope.Value<string>("provider")+" / "+envelope.Value<string>("model"),true);
            }catch{}
        }

        private void ShowError(string text)
        { Waiting=false;View.SetConversation(text,"SYSTEM MESSAGE / no world state was changed",false,false,true);Trace.Record("request_error",new{text},text); }

        public void ContinueDecision()
        {
            if(Waiting || !State.HasPendingDecision)return;
            var decision=State.PendingDecision;
            if(decision.action=="inspect" && (!CanReach(cameraExperiment.pointA.position)||!CanReach(cameraExperiment.pointB.position)))
            {AbortCurrentRequest();ShowError("The observation point is no longer reachable. Clear the route and try again.");return;}
            if(!State.TryContinuePending(out var command,out var reason)) {ShowError("The pending action is no longer valid: "+reason);return;}
            history.Add(new DialogueEntry("user",lastInput));history.Add(new DialogueEntry("assistant",decision.dialogue));
            currentRequest=null;
            View.ShowDialogue(false);dialoguePause?.Dispose();dialoguePause=null;UpdateCursor();
            if(command!=null) actionRoutine=StartCoroutine(Execute(command));
        }

        private IEnumerator Execute(ActionCommand command)
        {
            while(Pauses.IsPaused)yield return null;
            Trace.Record("action_started",command,command.action+" "+command.target_id);
            if(command.action=="follow")
            {
                lin.Follow(player.transform);
                Complete(command,true,"Following mode started; Lin will track the player's reachable position.");
            }
            else if(command.action=="inspect" && command.target_id=="camera_01")
            {
                bool success=false;string result="unknown",instance=null;
                yield return cameraExperiment.Run((ok,reason,id)=>{success=ok;result=reason;instance=id;});
                if(command.session_id!=State.Snapshot.session_id)yield break;
                if(success)
                {
                    bool added=State.TryVerifyEvidence(command.session_id,"camera-complete-"+instance,"E1",true,true,out var factReason);
                    Trace.Record("evidence_result",new {evidence_id="E1",instance,added,reason=factReason},added?"E1 added by completed world observation":factReason);
                    View.ShowToast("E1 verified. Ask Lin what he saw.");
                }
                Complete(command,success,result);
            }
            else Complete(command,false,"action_not_implemented");
            actionRoutine=null;
        }

        private void Complete(ActionCommand command,bool success,string reason)
        {
            var result=new ActionResult{session_id=command.session_id,action_id=command.action_id,event_id=Guid.NewGuid().ToString("N"),action=command.action,target_id=command.target_id,status=success?"succeeded":"failed",reason=reason};
            bool accepted=State.TryCompleteAction(result,out var rejection);
            Trace.Record("action_result",new{result,accepted,rejection},result.status+" / "+reason);
        }

        private bool CanReach(Vector3 destination)
        {
            if(lin.Agent==null || !lin.Agent.isOnNavMesh)return false;
            var path=new NavMeshPath();return lin.Agent.CalculatePath(destination,path)&&path.status==NavMeshPathStatus.PathComplete;
        }

        public void CloseDialogue()
        {AbortCurrentRequest();View.ShowDialogue(false);dialoguePause?.Dispose();dialoguePause=null;UpdateCursor();}
        private void AbortCurrentRequest()
        {
            if(currentRequest!=null)
            {
                var cancelled=currentRequest; State.CancelRequest(cancelled.request_id); currentRequest=null;
                Client.Abort(); if(requestRoutine!=null){StopCoroutine(requestRoutine);requestRoutine=null;}
                StartCoroutine(Client.CancelRemote(cancelled.session_id,cancelled.request_id));
                Trace.Record("request_cancelled",new{cancelled.request_id},"Pending reply cannot execute");
            }
            Waiting=false;
        }
        public void OpenMenu() {if(View.MenuVisible)return;menuPause=Pauses.Acquire("menu");View.ShowMenu(true);UpdateCursor();}
        public void CloseMenu() {View.ShowMenu(false);menuPause?.Dispose();menuPause=null;UpdateCursor();}
        public void CancelInspection()
        {
            var command=State.ActiveAction;
            if(command==null){View.ShowToast("There is no active inspection.");return;}
            if(actionRoutine!=null){StopCoroutine(actionRoutine);actionRoutine=null;}
            cameraExperiment.ResetExperiment();lin.Stop("Inspection cancelled");
            var result=new ActionResult{session_id=command.session_id,action_id=command.action_id,event_id=Guid.NewGuid().ToString("N"),action=command.action,target_id=command.target_id,status="cancelled",reason="Player cancelled the inspection before completion."};
            State.TryCompleteAction(result,out var reason);
            Trace.Record("action_cancelled",result,"No new evidence was granted");
            CloseMenu();View.ShowToast("Inspection cancelled. Existing evidence is unchanged.");
        }
        public void RestartSession()
        {
            AbortCurrentRequest();if(actionRoutine!=null){StopCoroutine(actionRoutine);actionRoutine=null;}
            lin.Stop();cameraExperiment.ResetExperiment();State.Reset();history.Clear();
            Pauses.Reset();dialoguePause=menuPause=introPause=terminalPause=null;
            View.ShowMenu(false);View.ShowDialogue(false);View.ShowIntro(false);View.Input.text="";
            player.ResetPosition(playerSpawn.position,playerSpawn.eulerAngles.y);lin.ResetAt(linSpawn.position);
            started=true;UpdateCursor();StartCoroutine(CheckService());
            Trace.Record("session_restarted",new{State.Snapshot.session_id},"New session; old responses are rejected");
        }
        private void UpdateCursor()
        {bool free=!started || Pauses.IsPaused;Cursor.visible=free;Cursor.lockState=free?CursorLockMode.None:CursorLockMode.Locked;}
        private void OnDestroy(){Time.timeScale=1;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
    }
}
